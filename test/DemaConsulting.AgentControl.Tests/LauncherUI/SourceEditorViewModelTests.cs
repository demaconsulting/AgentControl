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

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for <see cref="SourceEditorViewModel"/>, the add/edit dialog of a zip-package
///     folder or NuGet-package folder source.
/// </summary>
public class SourceEditorViewModelTests
{
    /// <summary>
    ///     Test that a new editor for each kind has the matching title and package ID section.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_Constructor_NewSourceOfEachKind_HasMatchingTitleAndSections()
    {
        // Act
        var zip = CreateEditor(PackageSourceType.Zip);
        var nuget = CreateEditor(PackageSourceType.NuGet);

        // Assert
        Assert.Equal("Add Zip-Package Folder", zip.Title);
        Assert.False(zip.IsNuGet);
        Assert.True(zip.IsNew);
        Assert.True(zip.IsAliasEditable);
        Assert.Equal("Add NuGet-Package Folder", nuget.Title);
        Assert.True(nuget.IsNuGet);
    }

    /// <summary>
    ///     Test that an empty new editor cannot be saved yet shows no error messages before the
    ///     user has touched a field.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_NewEmptyEditor_CannotSaveButShowsNoErrorsYet()
    {
        // Act
        var editor = CreateEditor(PackageSourceType.Zip);

        // Assert
        Assert.False(editor.CanSave);
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.Null(editor.AliasError);
        Assert.Null(editor.LocationError);
    }

    /// <summary>
    ///     Test that a valid zip source can be saved and the saved definition carries the trimmed
    ///     alias-independent values and no package IDs.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_SaveCommand_ValidZipSource_RaisesSavedWithDefinition()
    {
        // Arrange
        var editor = CreateEditor(PackageSourceType.Zip);
        editor.Alias = "Contoso";
        editor.Location = "  " + FullPath("zips") + "  ";
        PackageSourceDefinition? saved = null;
        editor.Saved += (_, definition) => saved = definition;

        // Act
        Assert.True(editor.CanSave);
        editor.SaveCommand.Execute(null);

        // Assert
        Assert.NotNull(saved);
        Assert.Equal("Contoso", saved.Alias);
        Assert.Equal(PackageSourceType.Zip, saved.Type);
        Assert.Equal(FullPath("zips"), saved.Location);
        Assert.Empty(saved.PackageIds);
    }

    /// <summary>
    ///     Test that alias problems are reported: blank, invalid characters, and a duplicate of an
    ///     existing alias ignoring case.
    /// </summary>
    [Theory]
    [InlineData("   ", "Enter an alias")]
    [InlineData("bad/alias", "Use letters")]
    [InlineData("Taken", "already exists")]
    [InlineData("taken", "already exists")]
    public void SourceEditorViewModel_Alias_Invalid_ReportsProblemAndBlocksSave(string alias, string expected)
    {
        // Arrange
        var editor = new SourceEditorViewModel(
            PackageSourceType.Zip, null, existing => string.Equals(existing, "Taken", StringComparison.OrdinalIgnoreCase));
        editor.Location = FullPath("zips");

        // Act
        editor.Alias = alias;

        // Assert
        Assert.Contains(expected, editor.AliasError, StringComparison.Ordinal);
        Assert.False(editor.CanSave);
    }

    /// <summary>
    ///     Test that location problems are reported: blank, a web address, a relative path, and a
    ///     location that embeds credentials, the last without repeating the secret.
    /// </summary>
    [Theory]
    [InlineData("   ", "Enter the folder")]
    [InlineData("https://feeds.example.com/index.json", "not a web address")]
    [InlineData("relative/folder", "full path")]
    [InlineData("https://user:SentinelSecretValue@feeds.example.com/index.json", "must not contain")]
    public void SourceEditorViewModel_Location_Invalid_ReportsProblemAndBlocksSave(string location, string expected)
    {
        // Arrange
        var editor = CreateEditor(PackageSourceType.Zip);
        editor.Alias = "Contoso";

        // Act
        editor.Location = location;

        // Assert
        Assert.Contains(expected, editor.LocationError, StringComparison.Ordinal);
        Assert.DoesNotContain("SentinelSecretValue", editor.LocationError, StringComparison.Ordinal);
        Assert.False(editor.CanSave);
    }

    /// <summary>
    ///     Test that a NuGet folder source needs at least one package ID before it can be saved,
    ///     and that the requirement is shown.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_NuGetSource_NoPackageIds_CannotSaveUntilOneIsAdded()
    {
        // Arrange: valid alias and folder, no package IDs
        var editor = CreateEditor(PackageSourceType.NuGet);
        editor.Alias = "Feed";
        editor.Location = FullPath("feed");

        // Assert: blocked, with the reason
        Assert.False(editor.CanSave);
        Assert.Contains("at least one package ID", editor.PackageIdsError, StringComparison.Ordinal);

        // Act: add one
        editor.NewPackageId = "contoso.agents";
        editor.AddPackageIdCommand.Execute(null);

        // Assert
        Assert.True(editor.CanSave);
        Assert.Null(editor.PackageIdsError);
        Assert.Equal(["contoso.agents"], editor.PackageIds);
        Assert.Equal(string.Empty, editor.NewPackageId);
    }

    /// <summary>
    ///     Test that a package ID that is invalid or already in the list cannot be added.
    /// </summary>
    [Theory]
    [InlineData("has space", "letters, digits")]
    [InlineData("-leading", "letters, digits")]
    [InlineData("Contoso.Agents", "already in the list")]
    public void SourceEditorViewModel_AddPackageId_InvalidOrDuplicate_IsRefused(string id, string expected)
    {
        // Arrange: one ID already listed
        var editor = CreateEditor(PackageSourceType.NuGet);
        editor.NewPackageId = "contoso.agents";
        editor.AddPackageIdCommand.Execute(null);

        // Act
        editor.NewPackageId = id;

        // Assert: reported, and executing the add command changes nothing
        Assert.Contains(expected, editor.NewPackageIdError, StringComparison.Ordinal);
        Assert.False(editor.AddPackageIdCommand.CanExecute(null));
        Assert.Single(editor.PackageIds);
    }

    /// <summary>
    ///     Test that removing the selected package ID shortens the list, and that the remove
    ///     command is disabled without a selection.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_RemovePackageId_RemovesSelectedId()
    {
        // Arrange: two IDs listed
        var editor = CreateEditor(PackageSourceType.NuGet);
        foreach (var id in new[] { "a.agents", "b.agents" })
        {
            editor.NewPackageId = id;
            editor.AddPackageIdCommand.Execute(null);
        }

        Assert.False(editor.RemovePackageIdCommand.CanExecute(null));

        // Act
        editor.SelectedPackageId = "a.agents";
        Assert.True(editor.RemovePackageIdCommand.CanExecute(null));
        editor.RemovePackageIdCommand.Execute(null);

        // Assert
        Assert.Equal(["b.agents"], editor.PackageIds);
        Assert.Null(editor.SelectedPackageId);
    }

    /// <summary>
    ///     Test that a valid NuGet source is saved with its package IDs.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_SaveCommand_ValidNuGetSource_SavesPackageIds()
    {
        // Arrange
        var editor = CreateEditor(PackageSourceType.NuGet);
        editor.Alias = "Feed";
        editor.Location = FullPath("feed");
        foreach (var id in new[] { "a.agents", "b.agents" })
        {
            editor.NewPackageId = id;
            editor.AddPackageIdCommand.Execute(null);
        }

        PackageSourceDefinition? saved = null;
        editor.Saved += (_, definition) => saved = definition;

        // Act
        editor.SaveCommand.Execute(null);

        // Assert
        Assert.NotNull(saved);
        Assert.Equal(PackageSourceType.NuGet, saved.Type);
        Assert.Equal(["a.agents", "b.agents"], saved.PackageIds);
    }

    /// <summary>
    ///     Test that editing an existing source keeps its alias fixed, never reports the source's
    ///     own alias as a duplicate, preserves settings the dialog does not show, and does not
    ///     modify the source it was given.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_EditExistingSource_AliasFixedAndOtherSettingsPreserved()
    {
        // Arrange: a NuGet source with settings the dialog does not show
        var existing = new PackageSourceDefinition
        {
            Alias = "Feed",
            Type = PackageSourceType.NuGet,
            Location = FullPath("feed"),
            PackageIds = ["a.agents"],
            RequireSignedPackages = true,
            SignerFingerprint = "ABCDEF"
        };
        var editor = new SourceEditorViewModel(PackageSourceType.Zip, existing, _ => true);
        PackageSourceDefinition? saved = null;
        editor.Saved += (_, definition) => saved = definition;

        // Act: change the folder and add an ID
        editor.Location = FullPath("feed2");
        editor.NewPackageId = "b.agents";
        editor.AddPackageIdCommand.Execute(null);
        editor.SaveCommand.Execute(null);

        // Assert: type kept (the constructor's type is ignored when editing), alias fixed,
        // hidden settings carried over, the original untouched
        Assert.False(editor.IsNew);
        Assert.False(editor.IsAliasEditable);
        Assert.True(editor.IsNuGet);
        Assert.Equal("Edit NuGet-Package Folder", editor.Title);
        Assert.NotNull(saved);
        Assert.Equal("Feed", saved.Alias);
        Assert.Equal(FullPath("feed2"), saved.Location);
        Assert.Equal(["a.agents", "b.agents"], saved.PackageIds);
        Assert.True(saved.RequireSignedPackages);
        Assert.Equal("ABCDEF", saved.SignerFingerprint);
        Assert.Equal(FullPath("feed"), existing.Location);
        Assert.Equal(["a.agents"], existing.PackageIds);
    }

    /// <summary>
    ///     Test that an invalid editor refuses to save even when the save command is executed
    ///     directly.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_SaveCommand_InvalidEditor_DoesNotRaiseSaved()
    {
        // Arrange
        var editor = CreateEditor(PackageSourceType.Zip);
        var raised = false;
        editor.Saved += (_, _) => raised = true;

        // Act
        editor.SaveCommand.Execute(null);

        // Assert
        Assert.False(raised);
    }

    /// <summary>
    ///     Test that constructing without an alias check throws ArgumentNullException.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_Constructor_NullAliasCheck_ThrowsArgumentNullException()
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new SourceEditorViewModel(PackageSourceType.Zip, null, null!));
    }

    /// <summary>
    ///     Creates a new-source editor with no aliases taken.
    /// </summary>
    private static SourceEditorViewModel CreateEditor(PackageSourceType type) => new(type, null, _ => false);

    /// <summary>
    ///     Builds a fully-qualified folder path valid on every platform the tests run on.
    /// </summary>
    private static string FullPath(string name) => Path.Combine(Path.GetTempPath(), "agentcontrol_source_editor", name);
}
