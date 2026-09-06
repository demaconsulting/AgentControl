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

using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for <see cref="SettingsWindowViewModel"/>.
/// </summary>
public class SettingsWindowViewModelTests
{
    /// <summary>
    ///     Test that the constructor seeds every editable property from the initial settings.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_Constructor_SeedsPropertiesFromInitialSettings()
    {
        // Arrange: a fully-populated initial settings object
        var initial = new AppSettings
        {
            PackageSourcePath = @"\\share\packages",
            GitExecutablePath = @"C:\tools\git.exe",
            AgentTool = AgentToolKind.Cursor,
            CustomAgentCommand = "some-command",
            ShellPreference = "pwsh"
        };

        // Act: construct the view model
        var viewModel = new SettingsWindowViewModel(initial, _ => { });

        // Assert: every property matches the initial settings
        Assert.Equal(@"\\share\packages", viewModel.PackageSourcePath);
        Assert.Equal(@"C:\tools\git.exe", viewModel.GitExecutablePath);
        Assert.Equal(AgentToolKind.Cursor, viewModel.AgentTool);
        Assert.Equal("some-command", viewModel.CustomAgentCommand);
        Assert.Equal("pwsh", viewModel.ShellPreference);
    }

    /// <summary>
    ///     Test that IsCustomAgentToolSelected is false for a well-known agent tool.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_IsCustomAgentToolSelected_WellKnownTool_ReturnsFalse()
    {
        // Arrange: an initial CopilotCli selection
        var viewModel = new SettingsWindowViewModel(new AppSettings { AgentTool = AgentToolKind.CopilotCli }, _ => { });

        // Assert: the custom-command textbox should not be shown
        Assert.False(viewModel.IsCustomAgentToolSelected);
    }

    /// <summary>
    ///     Test that IsCustomAgentToolSelected becomes true when AgentTool is set to Custom, and
    ///     raises PropertyChanged for both properties.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_AgentTool_SetToCustom_TogglesIsCustomAgentToolSelected()
    {
        // Arrange: a view model starting on a well-known tool, observed for property changes
        var viewModel = new SettingsWindowViewModel(new AppSettings { AgentTool = AgentToolKind.CopilotCli }, _ => { });
        var changedProperties = new List<string>();
        viewModel.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName ?? string.Empty);

        // Act: select Custom
        viewModel.AgentTool = AgentToolKind.Custom;

        // Assert: the custom-command textbox should now be shown, and both properties raised
        // PropertyChanged
        Assert.True(viewModel.IsCustomAgentToolSelected);
        Assert.Contains(nameof(SettingsWindowViewModel.AgentTool), changedProperties);
        Assert.Contains(nameof(SettingsWindowViewModel.IsCustomAgentToolSelected), changedProperties);
    }

    /// <summary>
    ///     Test that Save builds an AppSettings from the current properties, invokes the
    ///     onSave callback, and raises Saved.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_SaveCommand_Execute_InvokesOnSaveWithCurrentValuesAndRaisesSaved()
    {
        // Arrange: a view model with edited values, capturing the settings passed to onSave
        AppSettings? savedSettings = null;
        var viewModel = new SettingsWindowViewModel(new AppSettings(), settings => savedSettings = settings)
        {
            PackageSourcePath = @"D:\packages",
            GitExecutablePath = "git",
            AgentTool = AgentToolKind.ClaudeCode,
            ShellPreference = "cmd"
        };
        var savedRaised = false;
        viewModel.Saved += (_, _) => savedRaised = true;

        // Act: execute the save command
        viewModel.SaveCommand.Execute(null);

        // Assert: onSave received the edited values, and Saved was raised
        Assert.NotNull(savedSettings);
        Assert.Equal(@"D:\packages", savedSettings.PackageSourcePath);
        Assert.Equal("git", savedSettings.GitExecutablePath);
        Assert.Equal(AgentToolKind.ClaudeCode, savedSettings.AgentTool);
        Assert.Equal("cmd", savedSettings.ShellPreference);
        Assert.True(savedRaised);
    }

    /// <summary>
    ///     Test that constructing with a null initial settings throws ArgumentNullException.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_Constructor_NullInitial_ThrowsArgumentNullException()
    {
        // Act / Assert: a null initial settings object is rejected
        Assert.Throws<ArgumentNullException>(() => new SettingsWindowViewModel(null!, _ => { }));
    }
}
