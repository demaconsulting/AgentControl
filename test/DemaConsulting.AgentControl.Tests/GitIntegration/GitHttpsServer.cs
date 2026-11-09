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

using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.Tests.GitIntegration;

/// <summary>
///     A private Git server for tests: an in-process Kestrel server on a fresh loopback port, speaking TLS with
///     a throw-away self-signed certificate and the smart HTTP protocol of Git (served by the real
///     <c>git upload-pack</c> over a bare repository), that answers every request without the right HTTP Basic
///     credentials with 401, as a private repository on a Git host does.
/// </summary>
/// <remarks>
///     Together with a test <c>credential.helper</c> in an isolated global git configuration (and
///     <c>http.sslVerify=false</c> there, because the certificate is self-signed), it lets a test show what
///     really happens when git is asked for a credential: whether it is asked at all, and whether work that
///     must never prompt does. Every request is recorded (<see cref="Requests"/>). Only the read side of the
///     protocol is served (the advertisement of the refs and <c>upload-pack</c>), which is all a package
///     source needs. Configure before the first request; thread-safe for the request recording.
/// </remarks>
internal sealed class GitHttpsServer : IAsyncDisposable
{
    private static readonly Lazy<string> EmptyConfiguration = new(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentcontrol-git-server-{Guid.NewGuid():N}.gitconfig");
        File.WriteAllText(path, string.Empty);
        return path;
    });

    /// <summary>
    ///     The recorded requests, as <c>METHOD /path</c> followed by <c>(signed in)</c> or <c>(anonymous)</c>.
    /// </summary>
    private readonly List<string> _requests = [];

    /// <summary>
    ///     The running web application.
    /// </summary>
    private readonly WebApplication _app;

    /// <summary>
    ///     The bare repository that is served.
    /// </summary>
    private readonly string _bareRepository;

    /// <summary>
    ///     The user name and password a request must present, or <see langword="null"/> for none.
    /// </summary>
    private readonly string? _expectedAuthorization;

    /// <summary>
    ///     Initializes the server around a built web application.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="bareRepository">The bare repository to serve.</param>
    /// <param name="user">The user name a request must present.</param>
    /// <param name="password">The password a request must present.</param>
    private GitHttpsServer(WebApplication app, string bareRepository, string user, string password)
    {
        _app = app;
        _bareRepository = bareRepository;
        _expectedAuthorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));
    }

    /// <summary>
    ///     Gets the address of the served repository, such as <c>https://127.0.0.1:5123/agents.git</c>: an
    ///     address a Git source accepts.
    /// </summary>
    public string RepositoryUrl { get; private set; } = string.Empty;

    /// <summary>
    ///     Gets the requests received so far, as <c>METHOD /path</c> followed by <c>(signed in)</c> or
    ///     <c>(anonymous)</c>.
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
    ///     Gets how many requests carried the right credentials and were answered.
    /// </summary>
    public int SignedInRequestCount => Requests.Count(request => request.EndsWith("(signed in)", StringComparison.Ordinal));

    /// <summary>
    ///     Gets how many requests carried no credentials and were refused with 401.
    /// </summary>
    public int AnonymousRequestCount => Requests.Count(request => request.EndsWith("(anonymous)", StringComparison.Ordinal));

    /// <summary>
    ///     Starts a server for a bare repository on a fresh loopback port.
    /// </summary>
    /// <param name="bareRepository">The bare repository to serve.</param>
    /// <param name="user">The user name a request must present.</param>
    /// <param name="password">The password a request must present.</param>
    /// <returns>The running server.</returns>
    public static async Task<GitHttpsServer> StartAsync(string bareRepository, string user, string password)
    {
        var certificate = CreateSelfSignedCertificate();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(
            options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
        var app = builder.Build();
        var server = new GitHttpsServer(app, bareRepository, user, password);
        app.Run(server.HandleAsync);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        server.RepositoryUrl = address.TrimEnd('/') + "/agents.git";
        return server;
    }

    /// <summary>
    ///     Creates the isolated global git configuration a test gives to git: certificate checks off (the
    ///     server's certificate is self-signed), no system configuration, and one credential helper that
    ///     behaves like Git Credential Manager does - it answers a request for a credential only when it may
    ///     show its sign-in UI (when <c>GCM_INTERACTIVE</c> is not <c>never</c>) - and records every question
    ///     it is asked in a log file.
    /// </summary>
    /// <param name="folder">The folder to write the configuration and the log to.</param>
    /// <param name="user">The user name the helper supplies.</param>
    /// <param name="password">The password the helper supplies.</param>
    /// <returns>The path of the configuration file and the path of the helper's log; each line of the log is
    ///     <c>asked</c> (the helper was asked and could not prompt) or <c>prompted</c> (it was asked, was
    ///     allowed to prompt, and supplied the credential).</returns>
    public static (string ConfigurationPath, string HelperLogPath) CreateUserConfiguration(
        string folder, string user, string password)
    {
        var log = Path.Combine(folder, "helper.log").Replace('\\', '/');
        var configuration = Path.Combine(folder, "user.gitconfig");
        var helper = "!f() { test \\\"$1\\\" = get || exit 0; " +
                     $"if [ \\\"$GCM_INTERACTIVE\\\" = never ]; then echo asked >> {log}; exit 0; fi; " +
                     $"echo prompted >> {log}; echo username={user}; echo password={password}; }}; f";
        File.WriteAllText(
            configuration,
            // cspell:ignore schannel
            "[http]\n\tsslVerify = false\n\tschannelCheckRevoke = false\n" +
            $"[credential]\n\thelper = \"{helper}\"\n");
        return (configuration, Path.Combine(folder, "helper.log"));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>
    ///     Creates a self-signed certificate for the loopback address that Kestrel can use.
    /// </summary>
    /// <returns>The certificate with its private key.</returns>
    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        using var selfSigned = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        // Kestrel on Windows needs the key in a persisted form, so the certificate is re-imported from a PFX
        return X509CertificateLoader.LoadPkcs12(selfSigned.Export(X509ContentType.Pfx), null);
    }

    /// <summary>
    ///     Answers one request: 401 without the right credentials, otherwise the read side of smart HTTP.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <returns>A task that completes when the response is written.</returns>
    private async Task HandleAsync(HttpContext context)
    {
        var signedIn = string.Equals(context.Request.Headers.Authorization.ToString(), _expectedAuthorization, StringComparison.Ordinal);
        lock (_requests)
        {
            _requests.Add($"{context.Request.Method} {context.Request.Path} " + (signedIn ? "(signed in)" : "(anonymous)"));
        }

        if (!signedIn)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"git\"";
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (HttpMethods.IsGet(context.Request.Method) && path.EndsWith("/info/refs", StringComparison.Ordinal) &&
            context.Request.Query["service"] == "git-upload-pack")
        {
            var advertisement = await RunUploadPackAsync(["--stateless-rpc", "--advertise-refs", _bareRepository], null);
            context.Response.ContentType = "application/x-git-upload-pack-advertisement";
            await context.Response.Body.WriteAsync(
                Encoding.ASCII.GetBytes("001e# service=git-upload-pack\n0000"), context.RequestAborted);
            await context.Response.Body.WriteAsync(advertisement, context.RequestAborted);
            return;
        }

        if (HttpMethods.IsPost(context.Request.Method) && path.EndsWith("/git-upload-pack", StringComparison.Ordinal))
        {
            using var body = new MemoryStream();
            Stream input = context.Request.Body;
            if (string.Equals(context.Request.Headers.ContentEncoding, "gzip", StringComparison.OrdinalIgnoreCase))
            {
                input = new GZipStream(input, CompressionMode.Decompress);
            }

            await input.CopyToAsync(body, context.RequestAborted);
            var result = await RunUploadPackAsync(["--stateless-rpc", _bareRepository], body.ToArray());
            context.Response.ContentType = "application/x-git-upload-pack-result";
            await context.Response.Body.WriteAsync(result, context.RequestAborted);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    /// <summary>
    ///     Runs <c>git upload-pack</c> and returns what it writes.
    /// </summary>
    /// <param name="arguments">The arguments after <c>upload-pack</c>.</param>
    /// <param name="input">The bytes for standard input, or <see langword="null"/> for none.</param>
    /// <returns>The standard output.</returns>
    private static async Task<byte[]> RunUploadPackAsync(string[] arguments, byte[]? input)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("upload-pack");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = EmptyConfiguration.Value;
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("git did not start.");
        var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output);
        var error = process.StandardError.ReadToEndAsync();
        if (input is not null)
        {
            await process.StandardInput.BaseStream.WriteAsync(input);
        }

        process.StandardInput.Close();
        await copy;
        await process.WaitForExitAsync();
        _ = await error;
        return output.ToArray();
    }
}
