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

using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.AgentControl.Tests.PackageCredentials;
using DemaConsulting.NuGet.Caching;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for the package-signature options and the "Forget saved sign-in" action of
///     <see cref="SourceEditorViewModel"/>: when the options are offered and enabled, how the
///     certificate fingerprint is validated and normalized, what is saved, and what forgetting a
///     saved sign-in does and says.
/// </summary>
public class SourceEditorSignatureViewModelTests
{
    /// <summary>
    ///     A SHA-256 fingerprint as certificate viewers print it.
    /// </summary>
    private const string ColonFingerprint =
        "a3:af:7a:f1:1e:bb:8e:f7:29:d2:d9:15:48:50:97:17:e7:e0:ff:55:a1:29:ab:c3:ae:aa:8a:69:40:26:76:41";

    /// <summary>
    ///     The same fingerprint, normalized.
    /// </summary>
    private const string NormalizedFingerprint = "A3AF7AF11EBB8EF729D2D91548509717E7E0FF55A129ABC3AEAA8A6940267641";

    /// <summary>
    ///     A valid https feed address.
    /// </summary>
    private const string FeedAddress = "https://feeds.example.com:8443/v3/index.json";

    /// <summary>
    ///     Test that the signature options are offered for NuGet folders and servers but not for zip
    ///     folders (a zip carries no signature), and start unchecked with the fingerprint field
    ///     unusable.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_SignatureOptions_OfferedForNuGetOnlyAndOffByDefault()
    {
        // Act
        var server = CreateServerEditor();
        var folder = new SourceEditorViewModel(PackageSourceType.NuGet, null, _ => false);
        var zip = new SourceEditorViewModel(PackageSourceType.Zip, null, _ => false);

        // Assert
        Assert.True(server.ShowSignatureOptions);
        Assert.True(folder.ShowSignatureOptions);
        Assert.False(zip.ShowSignatureOptions);
        Assert.False(server.RequireSignedPackages);
        Assert.False(server.IsFingerprintEnabled);
        Assert.Equal(string.Empty, server.SignerFingerprint);
        Assert.Contains("any validly signed package", SourceEditorViewModel.SignatureHelpText, StringComparison.Ordinal);
        Assert.Contains("unrelated publishers", SourceEditorViewModel.SignatureHelpText, StringComparison.Ordinal);
        Assert.Contains("pinning your publisher's certificate", SourceEditorViewModel.SignatureHelpText, StringComparison.Ordinal);
        Assert.Contains("dotnet nuget verify", SourceEditorViewModel.FingerprintHelpText, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that where signature verification is supported the checkbox is enabled and explains
    ///     what it does, and ticking it enables the fingerprint field.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_RequireSigned_SupportedPlatform_CheckboxEnabledAndTickingEnablesTheFingerprint()
    {
        // Arrange
        var editor = CreateServerEditor(signaturesSupported: true);

        // Assert before
        Assert.True(editor.IsRequireSignedEnabled);
        Assert.Contains("not validly signed", editor.RequireSignedTooltip, StringComparison.Ordinal);
        Assert.False(editor.IsFingerprintEnabled);

        // Act
        editor.RequireSignedPackages = true;

        // Assert
        Assert.True(editor.IsFingerprintEnabled);
    }

    /// <summary>
    ///     Test that where signature verification is not supported the checkbox is disabled with the
    ///     documented explanation, and the fingerprint field stays unusable.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_RequireSigned_UnsupportedPlatform_CheckboxDisabledWithExplanation()
    {
        // Act
        var editor = CreateServerEditor(signaturesSupported: false);

        // Assert
        Assert.False(editor.IsRequireSignedEnabled);
        Assert.Equal("Package signature verification is not supported on this platform", editor.RequireSignedTooltip);
        Assert.False(editor.IsFingerprintEnabled);
    }

    /// <summary>
    ///     Test that a platform whose capability probe cannot run (the probe throws) is treated as
    ///     unsupported by the editor: the checkbox is disabled with the explanation and nothing
    ///     throws.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_RequireSigned_ProbeFails_CheckboxDisabledWithExplanationAndNoThrow()
    {
        // Arrange: support decided by a probe that fails the way a broken crypto stack does
        var supported = SignatureVerificationSupport.Evaluate(
            isWindows: false, isLinux: true, isMacOS: false, () => throw new DllNotFoundException("libssl"));

        // Act
        var editor = CreateServerEditor(signaturesSupported: supported);

        // Assert
        Assert.False(editor.IsRequireSignedEnabled);
        Assert.Equal("Package signature verification is not supported on this platform", editor.RequireSignedTooltip);
        Assert.False(editor.IsFingerprintEnabled);
    }

    /// <summary>
    ///     Test that an existing source that already requires signatures keeps its settings when
    ///     edited on a platform that cannot verify them: the choice is shown (disabled) and saved
    ///     unchanged, never silently dropped.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_EditExistingRequiringSource_UnsupportedPlatform_KeepsTheSettings()
    {
        // Arrange
        var existing = new PackageSourceDefinition
        {
            Alias = "Feed",
            Type = PackageSourceType.NuGet,
            Location = FeedAddress,
            PackageIds = ["contoso-agents"],
            RequireSignedPackages = true,
            SignerFingerprint = NormalizedFingerprint
        };
        var editor = new SourceEditorViewModel(
            PackageSourceType.NuGet, existing, _ => false, signatureVerificationSupported: false);
        PackageSourceDefinition? saved = null;
        editor.Saved += (_, definition) => saved = definition;

        // Act
        editor.SaveCommand.Execute(null);

        // Assert
        Assert.True(editor.RequireSignedPackages);
        Assert.False(editor.IsRequireSignedEnabled);
        Assert.NotNull(saved);
        Assert.True(saved.RequireSignedPackages);
        Assert.Equal(NormalizedFingerprint, saved.SignerFingerprint);
    }

    /// <summary>
    ///     Test that an unreadable fingerprint is reported - only after the user typed it - and
    ///     blocks saving, while an empty one is fine and a readable one in any common spelling is
    ///     accepted.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_Fingerprint_ValidationFollowsTheRules()
    {
        // Arrange
        var editor = CreateFilledServerEditor();
        editor.RequireSignedPackages = true;
        Assert.Null(editor.FingerprintError);
        Assert.True(editor.CanSave);

        // Act / Assert: garbage is reported and blocks the save
        editor.SignerFingerprint = "not a fingerprint";
        Assert.Contains("64 hexadecimal characters", editor.FingerprintError, StringComparison.Ordinal);
        Assert.False(editor.CanSave);
        Assert.False(editor.SaveCommand.CanExecute(null));

        // Act / Assert: too short is garbage too
        editor.SignerFingerprint = "A3:AF";
        Assert.NotNull(editor.FingerprintError);

        // Act / Assert: a readable fingerprint is accepted
        editor.SignerFingerprint = ColonFingerprint;
        Assert.Null(editor.FingerprintError);
        Assert.True(editor.CanSave);

        // Act / Assert: empty is fine (the pin is optional)
        editor.SignerFingerprint = string.Empty;
        Assert.Null(editor.FingerprintError);
        Assert.True(editor.CanSave);
    }

    /// <summary>
    ///     Test that a fingerprint that is not in use (signatures not required) neither blocks the
    ///     save nor is saved.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_Fingerprint_NotInUse_IsIgnoredAndNotSaved()
    {
        // Arrange: a bad fingerprint typed while signatures are required, then the box is unticked
        var editor = CreateFilledServerEditor();
        PackageSourceDefinition? saved = null;
        editor.Saved += (_, definition) => saved = definition;
        editor.RequireSignedPackages = true;
        editor.SignerFingerprint = "garbage";
        Assert.False(editor.CanSave);

        // Act
        editor.RequireSignedPackages = false;
        editor.SignerFingerprint = ColonFingerprint;
        editor.SaveCommand.Execute(null);

        // Assert
        Assert.Null(editor.FingerprintError);
        Assert.NotNull(saved);
        Assert.False(saved.RequireSignedPackages);
        Assert.Null(saved.SignerFingerprint);
    }

    /// <summary>
    ///     Test that a source saved with the requirement carries it and the fingerprint in its
    ///     normalized form (64 upper-case hexadecimal characters).
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_Save_RequireSignedWithFingerprint_SavesTheNormalizedFingerprint()
    {
        // Arrange
        var editor = CreateFilledServerEditor();
        PackageSourceDefinition? saved = null;
        editor.Saved += (_, definition) => saved = definition;
        editor.RequireSignedPackages = true;
        editor.SignerFingerprint = ColonFingerprint;

        // Act
        editor.SaveCommand.Execute(null);

        // Assert
        Assert.NotNull(saved);
        Assert.True(saved.RequireSignedPackages);
        Assert.Equal(NormalizedFingerprint, saved.SignerFingerprint);
    }

    /// <summary>
    ///     Test that a source saved with the requirement but no fingerprint pins nothing.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_Save_RequireSignedWithoutFingerprint_PinsNothing()
    {
        // Arrange
        var editor = CreateFilledServerEditor();
        PackageSourceDefinition? saved = null;
        editor.Saved += (_, definition) => saved = definition;
        editor.RequireSignedPackages = true;

        // Act
        editor.SaveCommand.Execute(null);

        // Assert
        Assert.NotNull(saved);
        Assert.True(saved.RequireSignedPackages);
        Assert.Null(saved.SignerFingerprint);
    }

    /// <summary>
    ///     Test that a zip source never saves a signature requirement, and a NuGet folder source can.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_Save_ZipNeverRequiresSignaturesAndFolderCan()
    {
        // Arrange
        var folderPath = Path.GetTempPath();
        var zip = new SourceEditorViewModel(PackageSourceType.Zip, null, _ => false)
        {
            Alias = "Zips",
            Location = folderPath
        };
        var folder = new SourceEditorViewModel(PackageSourceType.NuGet, null, _ => false)
        {
            Alias = "Folder",
            Location = folderPath,
            RequireSignedPackages = true
        };
        folder.NewPackageId = "contoso-agents";
        folder.AddPackageIdCommand.Execute(null);
        PackageSourceDefinition? zipSaved = null;
        PackageSourceDefinition? folderSaved = null;
        zip.Saved += (_, definition) => zipSaved = definition;
        folder.Saved += (_, definition) => folderSaved = definition;

        // Act
        zip.SaveCommand.Execute(null);
        folder.SaveCommand.Execute(null);

        // Assert
        Assert.False(zipSaved!.RequireSignedPackages);
        Assert.Null(zipSaved.SignerFingerprint);
        Assert.True(folderSaved!.RequireSignedPackages);
    }

    /// <summary>
    ///     Test that "Forget saved sign-in" is available for a server with an acceptable address and
    ///     a credential store to ask, and not for a folder, an unusable address or no store - with a
    ///     tooltip that says why.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_ForgetSavedSignIn_AvailableOnlyForAUsableServerWithACredentialStore()
    {
        // Arrange
        var context = new PackageCredentialContext(new SessionCredentialStore(), null, new FakeRememberedCredentialStore());

        // Act
        var usable = CreateServerEditor(credentials: context);
        usable.Location = FeedAddress;
        var invalidAddress = CreateServerEditor(credentials: context);
        invalidAddress.Location = "not an address";
        var noStore = CreateServerEditor(credentials: null);
        noStore.Location = FeedAddress;
        var folder = new SourceEditorViewModel(PackageSourceType.NuGet, null, _ => false, credentials: context)
        {
            Location = Path.GetTempPath()
        };

        // Assert
        Assert.True(usable.CanForgetSavedSignIn);
        Assert.True(usable.ForgetSavedSignInCommand.CanExecute(null));
        Assert.Contains("saved", usable.ForgetTooltip, StringComparison.Ordinal);
        Assert.False(invalidAddress.CanForgetSavedSignIn);
        Assert.Contains("valid feed address", invalidAddress.ForgetTooltip, StringComparison.Ordinal);
        Assert.False(noStore.CanForgetSavedSignIn);
        Assert.Contains("no Git credential helper", noStore.ForgetTooltip, StringComparison.Ordinal);
        Assert.False(folder.CanForgetSavedSignIn);
    }

    /// <summary>
    ///     Test that the command only asks the view to confirm - naming the server - and removes
    ///     nothing until the view confirms by calling the forget method.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_ForgetSavedSignIn_CommandAsksForConfirmationAndForgetsOnlyOnceConfirmed()
    {
        // Arrange
        var origin = FeedOrigin.Create(new Uri(FeedAddress));
        var usersStore = new FakeRememberedCredentialStore();
        usersStore.Save(origin, new NuGetCredential("svc", "pw-forget-editor-4420"));
        var editor = CreateServerEditor(credentials: new PackageCredentialContext(new SessionCredentialStore(), null, usersStore));
        editor.Location = FeedAddress;
        var questions = new List<string>();
        editor.ForgetConfirmationRequested += (_, question) => questions.Add(question);

        // Act: the button
        editor.ForgetSavedSignInCommand.Execute(null);

        // Assert: asked, nothing removed yet
        var question = Assert.Single(questions);
        Assert.Contains("https://feeds.example.com:8443", question, StringComparison.Ordinal);
        Assert.Contains("Git credential helper", question, StringComparison.Ordinal);
        Assert.NotNull(usersStore.Peek(origin));
        Assert.False(editor.HasForgetResult);

        // Act: the view confirmed
        await editor.ForgetSavedSignInAsync();

        // Assert
        Assert.Null(usersStore.Peek(origin));
        Assert.True(editor.HasForgetResult);
        Assert.Equal("The saved sign-in for https://feeds.example.com:8443 was forgotten.", editor.ForgetResultText);
    }

    /// <summary>
    ///     Test the outcomes the editor reports: nothing was saved, the credential store refused, and
    ///     the credential store failing outright - the last never leaks beyond a short explanation.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_ForgetSavedSignIn_ReportsNothingSavedRefusalAndFailureInWords()
    {
        // Arrange
        var origin = FeedOrigin.Create(new Uri(FeedAddress));
        var usersStore = new FakeRememberedCredentialStore();
        var editor = CreateServerEditor(credentials: new PackageCredentialContext(new SessionCredentialStore(), null, usersStore));
        editor.Location = FeedAddress;

        // Act / Assert: nothing saved
        await editor.ForgetSavedSignInAsync();
        Assert.Equal("No saved sign-in was found for https://feeds.example.com:8443.", editor.ForgetResultText);

        // Act / Assert: the store refuses to remove it
        usersStore.Save(origin, new NuGetCredential("svc", "pw-refuse-editor-4421"));
        usersStore.RefuseChanges = true;
        await editor.ForgetSavedSignInAsync();
        Assert.Contains("could not remove the saved sign-in", editor.ForgetResultText, StringComparison.Ordinal);
        Assert.DoesNotContain("pw-refuse-editor-4421", editor.ForgetResultText, StringComparison.Ordinal);

        // Act / Assert: the store throws
        usersStore.ThrowOnEveryCall = true;
        await editor.ForgetSavedSignInAsync();
        Assert.Contains("could not be forgotten", editor.ForgetResultText, StringComparison.Ordinal);
        Assert.True(editor.CanForgetSavedSignIn);
    }

    /// <summary>
    ///     Test that forgetting the saved sign-in also clears a connection-test result, which
    ///     described a connection made with the sign-in that was just removed.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_ForgetSavedSignIn_ClearsTheConnectionTestResult()
    {
        // Arrange: a successful connection test, then a forget
        var fake = new Tests.AgentPackageManagement.FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"));
        var editor = new SourceEditorViewModel(
            PackageSourceType.NuGet,
            null,
            _ => false,
            isServer: true,
            _ => fake,
            new PackageCredentialContext(new SessionCredentialStore(), null, new FakeRememberedCredentialStore()),
            signatureVerificationSupported: true)
        {
            Alias = "Feed",
            Location = FeedAddress
        };
        editor.NewPackageId = "contoso-agents";
        editor.AddPackageIdCommand.Execute(null);
        await editor.TestConnectionCommand.ExecuteAsync();
        Assert.True(editor.HasTestResult);

        // Act
        await editor.ForgetSavedSignInAsync();

        // Assert
        Assert.False(editor.HasTestResult);
        Assert.True(editor.HasForgetResult);
    }

    /// <summary>
    ///     Test that "Forget saved sign-in" is unavailable, with a tooltip that says why, when no
    ///     Git credential helper applies to the server - nothing can have been saved - and that
    ///     neither the command nor the forget method then does anything.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_ForgetSavedSignIn_NoCredentialHelper_UnavailableWithExplanation()
    {
        // Arrange
        var usersStore = new FakeRememberedCredentialStore { Available = false };
        var editor = CreateServerEditor(credentials: new PackageCredentialContext(new SessionCredentialStore(), null, usersStore));
        var asked = false;
        editor.ForgetConfirmationRequested += (_, _) => asked = true;

        // Act
        editor.Location = FeedAddress;
        await editor.HelperCheckCompleted;
        editor.ForgetSavedSignInCommand.Execute(null);
        await editor.ForgetSavedSignInAsync();

        // Assert
        Assert.False(editor.CanForgetSavedSignIn);
        Assert.False(editor.ForgetSavedSignInCommand.CanExecute(null));
        Assert.Equal(SourceEditorViewModel.NoHelperForgetTooltip, editor.ForgetTooltip);
        Assert.Contains("no Git credential helper is configured", editor.ForgetTooltip, StringComparison.Ordinal);
        Assert.False(asked);
        Assert.False(editor.HasForgetResult);
        Assert.Equal(0, usersStore.CountOf("reject"));
    }

    /// <summary>
    ///     Test that the action stays unavailable (with a "checking" explanation) while the check
    ///     for a helper is still running, becomes available when it finds one, and that a check
    ///     that fails counts as "no helper" without throwing.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_ForgetSavedSignIn_HelperCheckPendingThenAnswered_FollowsTheAnswer()
    {
        // Arrange: a check that waits to be released
        var release = new TaskCompletionSource();
        var usersStore = new FakeRememberedCredentialStore { AvailableGate = release.Task };
        var editor = CreateServerEditor(credentials: new PackageCredentialContext(new SessionCredentialStore(), null, usersStore));

        // Act / Assert: pending
        editor.Location = FeedAddress;
        Assert.False(editor.CanForgetSavedSignIn);
        Assert.Contains("Checking", editor.ForgetTooltip, StringComparison.Ordinal);

        // Act / Assert: answered "available"
        release.SetResult();
        await editor.HelperCheckCompleted;
        Assert.True(editor.CanForgetSavedSignIn);

        // Act / Assert: a different server whose check fails is "no helper", not an exception
        usersStore.AvailableGate = null;
        usersStore.ThrowOnEveryCall = true;
        editor.Location = "https://other.example.com/v3/index.json";
        await editor.HelperCheckCompleted;
        Assert.False(editor.CanForgetSavedSignIn);
        Assert.Equal(SourceEditorViewModel.NoHelperForgetTooltip, editor.ForgetTooltip);
    }

    /// <summary>
    ///     Test that the helper is asked once per server origin however the rest of the address is
    ///     edited, and again for a different origin.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_ForgetSavedSignIn_AddressEdits_AskTheHelperOncePerOrigin()
    {
        // Arrange
        var usersStore = new FakeRememberedCredentialStore();
        var editor = CreateServerEditor(credentials: new PackageCredentialContext(new SessionCredentialStore(), null, usersStore));

        // Act
        editor.Location = "https://feeds.example.com/v3/index.json";
        await editor.HelperCheckCompleted;
        editor.Location = "https://feeds.example.com/other/index.json";
        await editor.HelperCheckCompleted;
        editor.Location = "https://feeds.example.com/v3/index.json";
        await editor.HelperCheckCompleted;
        editor.Location = "https://another.example.com/v3/index.json";
        await editor.HelperCheckCompleted;

        // Assert
        Assert.Equal(2, usersStore.CountOf("available"));
        Assert.True(editor.CanForgetSavedSignIn);
    }

    /// <summary>
    ///     Test that the forget method does nothing when the action is not available (an unusable
    ///     address), and that the command asks nothing then either.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_ForgetSavedSignIn_NotAvailable_DoesNothing()
    {
        // Arrange
        var usersStore = new FakeRememberedCredentialStore();
        var editor = CreateServerEditor(credentials: new PackageCredentialContext(new SessionCredentialStore(), null, usersStore));
        editor.Location = "not an address";
        var asked = false;
        editor.ForgetConfirmationRequested += (_, _) => asked = true;

        // Act
        editor.ForgetSavedSignInCommand.Execute(null);
        await editor.ForgetSavedSignInAsync();

        // Assert
        Assert.False(asked);
        Assert.Empty(usersStore.Calls);
        Assert.False(editor.HasForgetResult);
    }

    /// <summary>
    ///     Test that an address the dialog would refuse to save - not ending in index.json, plain
    ///     http without the opt-in, or carrying credentials - never makes "Forget saved sign-in"
    ///     available and never reaches the credential helper, although its origin can be parsed.
    /// </summary>
    [Theory]
    [InlineData("https://feeds.example.com/v3/")]
    [InlineData("https://feeds.example.com/v3/feed.json")]
    [InlineData("http://feeds.example.com/v3/index.json")]
    [InlineData("https://feeds.example.com/v3/index.json?sig=sentinel-forget-1")]
    public async Task SourceEditorViewModel_CanForgetSavedSignIn_AddressNotAnAcceptableFeed_IsFalseAndHelperIsNeverAsked(
        string address)
    {
        // Arrange
        var usersStore = new FakeRememberedCredentialStore();
        var editor = CreateServerEditor(credentials: new PackageCredentialContext(new SessionCredentialStore(), null, usersStore));
        var asked = false;
        editor.ForgetConfirmationRequested += (_, _) => asked = true;

        // Act
        editor.Location = address;
        await editor.HelperCheckCompleted;
        editor.ForgetSavedSignInCommand.Execute(null);
        await editor.ForgetSavedSignInAsync();

        // Assert: the origin parses, but the address is not acceptable, so nothing is available or asked
        Assert.True(FeedOrigin.TryParse(address, out _));
        Assert.NotNull(editor.LocationError);
        Assert.False(editor.CanForgetSavedSignIn);
        Assert.False(editor.ForgetSavedSignInCommand.CanExecute(null));
        Assert.False(asked);
        Assert.Empty(usersStore.Calls);
        Assert.False(editor.HasForgetResult);
    }

    /// <summary>
    ///     Test that a plain http address becomes eligible for forgetting its saved sign-in once the
    ///     user opts in to insecure http, and stops being eligible when the opt-in is withdrawn.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_CanForgetSavedSignIn_InsecureHttpOptedIn_BecomesAvailable()
    {
        // Arrange
        var usersStore = new FakeRememberedCredentialStore();
        var editor = CreateServerEditor(credentials: new PackageCredentialContext(new SessionCredentialStore(), null, usersStore));
        editor.Location = "http://127.0.0.1:5000/v3/index.json";
        await editor.HelperCheckCompleted;
        Assert.False(editor.CanForgetSavedSignIn);
        Assert.Empty(usersStore.Calls);

        // Act: opt in
        editor.AllowInsecureHttp = true;
        await editor.HelperCheckCompleted;

        // Assert
        Assert.True(editor.CanForgetSavedSignIn);
        Assert.Equal(1, usersStore.CountOf("available"));

        // Act: withdraw the opt-in
        editor.AllowInsecureHttp = false;
        await editor.HelperCheckCompleted;

        // Assert
        Assert.False(editor.CanForgetSavedSignIn);
    }

    /// <summary>
    ///     Test that the signer fingerprint is accepted in every spelling a certificate viewer or
    ///     command-line tool prints (colons, dashes, spaces, lower case) and saved normalized.
    /// </summary>
    [Theory]
    [InlineData("a3-af-7a-f1-1e-bb-8e-f7-29-d2-d9-15-48-50-97-17-e7-e0-ff-55-a1-29-ab-c3-ae-aa-8a-69-40-26-76-41")]
    [InlineData("a3 af 7a f1 1e bb 8e f7 29 d2 d9 15 48 50 97 17 e7 e0 ff 55 a1 29 ab c3 ae aa 8a 69 40 26 76 41")]
    [InlineData("a3af7af11ebb8ef729d2d91548509717e7e0ff55a129abc3aeaa8a6940267641")]
    [InlineData("A3AF7AF11EBB8EF729D2D91548509717E7E0FF55A129ABC3AEAA8A6940267641")]
    public void SourceEditorViewModel_Fingerprint_DashesSpacesAndLowerCase_AreAccepted(string spelling)
    {
        // Arrange
        var editor = CreateFilledServerEditor();
        editor.RequireSignedPackages = true;
        PackageSourceDefinition? saved = null;
        editor.Saved += (_, definition) => saved = definition;

        // Act
        editor.SignerFingerprint = spelling;
        editor.SaveCommand.Execute(null);

        // Assert
        Assert.Null(editor.FingerprintError);
        Assert.NotNull(saved);
        Assert.Equal(NormalizedFingerprint, saved.SignerFingerprint);
    }

    /// <summary>
    ///     Creates a server editor.
    /// </summary>
    /// <param name="signaturesSupported">Whether the platform is taken to support signature
    ///     verification.</param>
    /// <param name="credentials">The credential facilities, or <see langword="null"/>.</param>
    /// <returns>The editor.</returns>
    private static SourceEditorViewModel CreateServerEditor(
        bool signaturesSupported = true, PackageCredentialContext? credentials = null) =>
        new(
            PackageSourceType.NuGet,
            null,
            _ => false,
            isServer: true,
            credentials: credentials,
            signatureVerificationSupported: signaturesSupported);

    /// <summary>
    ///     Creates a server editor that is otherwise ready to save.
    /// </summary>
    /// <returns>The editor.</returns>
    private static SourceEditorViewModel CreateFilledServerEditor()
    {
        var editor = CreateServerEditor();
        editor.Alias = "Feed";
        editor.Location = FeedAddress;
        editor.NewPackageId = "contoso-agents";
        editor.AddPackageIdCommand.Execute(null);
        return editor;
    }
}
