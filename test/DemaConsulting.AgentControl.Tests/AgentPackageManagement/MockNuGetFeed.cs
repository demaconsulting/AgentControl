// Copyright (c) DEMA Consulting
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.Tests.AgentPackageManagement;

/// <summary>
///     An in-process NuGet V3 feed served over plain HTTP by Kestrel on a fresh loopback port, for
///     tests that exercise the real NuGet client against a server (anonymous or HTTP Basic
///     protected) without any network access or TLS certificate.
/// </summary>
/// <remarks>
///     <para>
///     It serves the minimum a NuGet V3 client needs: the service index (<c>/v3/index.json</c>)
///     advertising a flat-container base address (and, optionally, a registration base address),
///     the flat-container version list of each package, and generated <c>.nupkg</c> files whose
///     content follows AgentControl's package layout (<c>content/.github/...</c>). When a user
///     name and password are required, every request without the matching Basic
///     <c>Authorization</c> header is answered 401 with <c>WWW-Authenticate: Basic</c>, which is
///     what a JFrog Artifactory or Azure Artifacts feed does. Every request is recorded
///     (<see cref="Requests"/>, <see cref="AuthorizationHeaders"/>) so tests can assert what the
///     client sent, in particular that a credential never reached the wrong server.
///     </para>
///     <para>
///     Each instance listens on its own port, because the NuGet client caches service indexes per
///     URL. The service index can be told to advertise its resources on a different feed
///     (<see cref="ResourceHost"/>) to simulate a server that sends the client to another origin.
///     Thread-safe for the request recording; configure the feed before the first request.
///     </para>
/// </remarks>
internal sealed class MockNuGetFeed : IAsyncDisposable
{
    /// <summary>
    ///     The packages served, by lower-case package ID, then version text.
    /// </summary>
    private readonly Dictionary<string, List<string>> _packages = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     The requests received, as <c>METHOD /path</c>.
    /// </summary>
    private readonly List<string> _requests = [];

    /// <summary>
    ///     The <c>Authorization</c> header values received (empty text for none).
    /// </summary>
    private readonly List<string> _authorizationHeaders = [];

    /// <summary>
    ///     The running web application.
    /// </summary>
    private readonly WebApplication _app;

    /// <summary>
    ///     Initializes the feed around an already built (not yet started) web application.
    /// </summary>
    /// <param name="app">The web application.</param>
    private MockNuGetFeed(WebApplication app)
    {
        _app = app;
    }

    /// <summary>
    ///     Gets the base URL of the feed, such as <c>http://127.0.0.1:5123</c>.
    /// </summary>
    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>
    ///     Gets the URL of the V3 service index, which is what a source definition names.
    /// </summary>
    public string IndexUrl => BaseUrl + "/v3/index.json";

    /// <summary>
    ///     Gets or sets the user name a request must present with Basic authentication, or
    ///     <see langword="null"/> for an anonymous feed.
    /// </summary>
    public string? RequiredUser { get; set; }

    /// <summary>
    ///     Gets or sets the password that goes with <see cref="RequiredUser"/>.
    /// </summary>
    public string? RequiredPassword { get; set; }

    /// <summary>
    ///     Gets or sets another feed whose base URL the service index advertises for the package
    ///     resources instead of this feed's own, simulating a server that sends clients to a
    ///     different origin; <see langword="null"/> advertises this feed.
    /// </summary>
    public MockNuGetFeed? ResourceHost { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the service index also advertises a
    ///     registration (package metadata) resource, which the NuGet library uses for listing
    ///     details; the default is <see langword="true"/>.
    /// </summary>
    public bool IncludeRegistration { get; set; } = true;

    /// <summary>
    ///     Gets or sets a value indicating whether every request is answered 403 (Forbidden),
    ///     whatever credentials it carries, as a server does for an account that may not read the
    ///     feed; the default is <see langword="false"/>.
    /// </summary>
    public bool ForbidAllRequests { get; set; }

    /// <summary>
    ///     Gets or sets the URL-encoded <c>X-TFS-ServiceError</c> header sent with every 403 (as Azure DevOps
    ///     explains a refusal), or <see langword="null"/> for none.
    /// </summary>
    public string? ForbiddenServiceError { get; set; }

    /// <summary>
    ///     Gets the requests received so far, as <c>METHOD /path</c>.
    /// </summary>
    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>
    ///     Gets the <c>Authorization</c> header value of every request received so far (empty text
    ///     for a request without one).
    /// </summary>
    public IReadOnlyList<string> AuthorizationHeaders
    {
        get
        {
            lock (_requests)
            {
                return [.. _authorizationHeaders];
            }
        }
    }

    /// <summary>
    ///     Gets how many requests were refused with 401.
    /// </summary>
    public int UnauthorizedCount { get; private set; }

    /// <summary>
    ///     Gets how many requests were refused with 403.
    /// </summary>
    public int ForbiddenCount { get; private set; }

    /// <summary>
    ///     Gets how many requests of the <c>.nupkg</c> download kind were served.
    /// </summary>
    public int DownloadCount { get; private set; }

    /// <summary>
    ///     Starts a feed on a fresh loopback port.
    /// </summary>
    /// <returns>The running feed, with no packages.</returns>
    public static async Task<MockNuGetFeed> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();

        // Port 0 lets the OS pick a free port; the real address is read back after the start
#pragma warning disable S5332 // Plain HTTP on loopback is the point of this test server: no TLS certificate needed
        builder.WebHost.UseUrls("http://127.0.0.1:0");
#pragma warning restore S5332
        var app = builder.Build();
        var feed = new MockNuGetFeed(app);
        feed.MapEndpoints();
        await app.StartAsync();
        feed.BaseUrl = app.Urls.First();
        return feed;
    }

    /// <summary>
    ///     Adds a package version to the feed; its <c>.nupkg</c> is generated when downloaded.
    /// </summary>
    /// <param name="packageId">The package ID.</param>
    /// <param name="version">The version text.</param>
    public void AddPackage(string packageId, string version)
    {
        if (!_packages.TryGetValue(packageId, out var versions))
        {
            versions = [];
            _packages[packageId] = versions;
        }

        versions.Add(version);
    }

    /// <summary>
    ///     Builds an agent package (<c>content/.github/agents/copilot.md</c>, standards, templates,
    ///     skills and release notes) as the bytes of a <c>.nupkg</c>.
    /// </summary>
    /// <param name="packageId">The package ID.</param>
    /// <param name="version">The version text.</param>
    /// <returns>The package bytes.</returns>
    public static byte[] BuildPackage(string packageId, string version)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, $"{packageId}.nuspec", BuildNuspec(packageId, version));
            AddEntry(archive, "content/.github/agents/copilot.md", $"agent content from the feed {version}");
            AddEntry(archive, "content/.github/standards/style.md", "standards content");
            AddEntry(archive, "content/.github/templates/template.md", "templates content");
            AddEntry(archive, "content/.github/skills/skill.md", "skills content");
            AddEntry(archive, "content/release-notes.md", $"# Release notes {version}\n");
        }

        return stream.ToArray();
    }

    /// <summary>
    ///     Stops the feed and releases its port.
    /// </summary>
    /// <returns>A task that completes when the server has stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>
    ///     Builds the text of a minimal nuspec.
    /// </summary>
    /// <param name="packageId">The package ID.</param>
    /// <param name="version">The version text.</param>
    /// <returns>The nuspec XML.</returns>
    private static string BuildNuspec(string packageId, string version) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\">\n" +
        "  <metadata>\n" +
        $"    <id>{packageId}</id>\n" +
        $"    <version>{version}</version>\n" +
        "    <authors>test</authors>\n" +
        "    <description>Test agent package</description>\n" +
        "  </metadata>\n" +
        "</package>\n";

    /// <summary>
    ///     Adds one UTF-8 text entry to an archive being built.
    /// </summary>
    /// <param name="archive">The archive.</param>
    /// <param name="path">The entry path.</param>
    /// <param name="content">The entry text.</param>
    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    /// <summary>
    ///     Registers the request recording, the authentication check and the NuGet V3 endpoints.
    /// </summary>
    private void MapEndpoints()
    {
        // Record every request, then refuse it (403 for all, 401 without the Basic credentials)
        _app.Use(async (context, next) =>
        {
            var header = context.Request.Headers.Authorization.ToString();
            lock (_requests)
            {
                _requests.Add($"{context.Request.Method} {context.Request.Path}");
                _authorizationHeaders.Add(header);
            }

            if (ForbidAllRequests)
            {
                lock (_requests)
                {
                    ForbiddenCount++;
                }

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                if (ForbiddenServiceError is { } serviceError)
                {
                    context.Response.Headers["X-TFS-ServiceError"] = serviceError;
                }

                return;
            }

            if (RequiredUser is not null && header != ExpectedAuthorization())
            {
                lock (_requests)
                {
                    UnauthorizedCount++;
                }

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Basic realm=\"mock-feed\"";
                return;
            }

            await next();
        });

        _app.MapGet("/v3/index.json", () => Results.Json(BuildServiceIndex()));
        _app.MapGet("/v3/flat/{id}/index.json", (string id) =>
            _packages.TryGetValue(id, out var versions)
                ? Results.Json(new { versions })
                : Results.NotFound());
        _app.MapGet("/v3/flat/{id}/{version}/{file}", (string id, string version, string file) =>
        {
            lock (_requests)
            {
                DownloadCount++;
            }

            return Results.Bytes(BuildPackage(id, version), "application/octet-stream");
        });
        _app.MapGet("/v3/reg/{id}/index.json", (string id) =>
            _packages.TryGetValue(id, out var versions)
                ? Results.Json(BuildRegistration(id, versions))
                : Results.NotFound());
    }

    /// <summary>
    ///     Gets the exact <c>Authorization</c> header value a request must carry.
    /// </summary>
    /// <returns>The expected header value.</returns>
    private string ExpectedAuthorization() =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{RequiredUser}:{RequiredPassword}"));

    /// <summary>
    ///     Builds the V3 service index document.
    /// </summary>
    /// <returns>The document to serialize.</returns>
    private object BuildServiceIndex()
    {
        var host = ResourceHost?.BaseUrl ?? BaseUrl;
        var resources = new List<object>
        {
            new Dictionary<string, object> { ["@id"] = host + "/v3/flat/", ["@type"] = "PackageBaseAddress/3.0.0" }
        };
        if (IncludeRegistration)
        {
            resources.Add(
                new Dictionary<string, object> { ["@id"] = host + "/v3/reg/", ["@type"] = "RegistrationsBaseUrl/3.6.0" });
        }

        return new { version = "3.0.0", resources };
    }

    /// <summary>
    ///     Builds the registration (package metadata) document of one package: a single page that
    ///     lists every version.
    /// </summary>
    /// <param name="id">The package ID.</param>
    /// <param name="versions">The versions of the package.</param>
    /// <returns>The document to serialize.</returns>
    private object BuildRegistration(string id, List<string> versions)
    {
        var items = versions.Select(version => (object)new Dictionary<string, object>
        {
            ["@id"] = $"{BaseUrl}/v3/reg/{id}/{version}.json",
            ["packageContent"] = $"{BaseUrl}/v3/flat/{id}/{version}/{id}.{version}.nupkg",
            ["catalogEntry"] = new Dictionary<string, object>
            {
                ["@id"] = $"{BaseUrl}/v3/catalog/{id}.{version}.json",
                ["id"] = id,
                ["version"] = version,
                ["listed"] = true,
                ["published"] = "2026-01-01T00:00:00Z"
            }
        }).ToArray();

        return new Dictionary<string, object>
        {
            ["count"] = 1,
            ["items"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["@id"] = $"{BaseUrl}/v3/reg/{id}/index.json#page/all",
                    ["count"] = items.Length,
                    ["lower"] = versions[0],
                    ["upper"] = versions[^1],
                    ["items"] = items
                }
            }
        };
    }
}
