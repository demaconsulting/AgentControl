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

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     End-to-end tests of the "Keep the shell open after the agent tool exits" setting: the
///     Settings window offers it (on by default, as in earlier versions), saves a changed choice,
///     and a launch with the choice cleared ends the shell together with the agent tool.
/// </summary>
/// <remarks>
///     The shell test relies on the arg-logger stub, which always exits with code 0, as the agent
///     tool. The keep-open case (shell stays) is covered by the <c>BuildProcessStartInfo</c> unit
///     tests: a shell that stays open on purpose cannot be left running by a test. Every other UI
///     test runs with the choice cleared by <c>TestSettingsWriter</c> so no console window outlives
///     it.
/// </remarks>
public sealed class ShellSettingTests
{
    /// <summary>
    ///     A settings file without the value reads as "keep the shell open" (backward compatible),
    ///     clearing the choice in Settings and saving writes <c>false</c>, and checking it again
    ///     writes <c>true</c>.
    /// </summary>
    [Fact]
    public void SettingsWindow_KeepShellOpenCheckBox_DefaultsOnClearedAndSavedIsPersisted()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: a settings file as an older version wrote it, without the value
        using var context = new AgentControlTestContext(
            customizeSettings: (settings, _) => settings.Remove("KeepShellOpen"));
        var mainWindow = context.Launch();

        // Act: open Settings
        var settingsWindow = OpenSettings(context, mainWindow);

        // Assert: the choice is on by default
        var checkBox = Find(settingsWindow, "KeepShellOpenCheckBox").AsCheckBox();
        Assert.Equal("Keep the shell open after the agent tool exits", checkBox.Name);
        Assert.True(checkBox.IsChecked);

        // Act: clear it and save
        checkBox.IsChecked = false;
        Find(settingsWindow, "SaveSettingsButton").AsButton().Invoke();

        // Assert: the choice was persisted as a boolean
        Assert.True(Retry.WhileFalse(
            () => ReadSettings(context).Contains("\"KeepShellOpen\": false", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);

        // Act: open Settings again, check it and save
        settingsWindow = OpenSettings(context, mainWindow);
        checkBox = Find(settingsWindow, "KeepShellOpenCheckBox").AsCheckBox();
        Assert.False(checkBox.IsChecked);
        checkBox.IsChecked = true;
        Find(settingsWindow, "SaveSettingsButton").AsButton().Invoke();

        // Assert: the new choice was persisted
        Assert.True(Retry.WhileFalse(
            () => ReadSettings(context).Contains("\"KeepShellOpen\": true", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
    }

    /// <summary>
    ///     With the choice cleared (as every UI test runs), clicking "Launch" runs the agent tool
    ///     in a shell that ends together with it: the stub records its invocation and then no
    ///     process launched by this context is left running.
    /// </summary>
    [Fact]
    public void LaunchButton_Click_KeepShellOpenOff_ShellEndsWhenAgentToolExits()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        using var context = new AgentControlTestContext();
        var mainWindow = context.Launch();

        // Act: launch the agent tool
        var launchButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("LaunchButton"))?.AsButton();
        Assert.NotNull(launchButton);
        launchButton.Invoke();

        // Assert: the stub ran (its record carries the agent-tool marker argument) ...
        var invocation = ArgLoggerLog.WaitForInvocation(
            context.ArgLoggerOutputFile,
            record => record.Arguments.Contains(TestSettingsWriter.AgentToolMarkerArgument),
            TimeSpan.FromSeconds(15));
        Assert.NotNull(invocation);

        // ... and the shell that ran it ends by itself, with nothing left for the safety net
        Assert.True(context.WaitForLaunchedShellToExit(TimeSpan.FromSeconds(15)));
    }

    /// <summary>
    ///     Opens the Settings window from the main window.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <param name="mainWindow">The main window.</param>
    /// <returns>The Settings window.</returns>
    private static AutomationElement OpenSettings(AgentControlTestContext context, Window mainWindow)
    {
        Find(mainWindow, "OpenSettingsButton").AsButton().Invoke();
        var settingsWindow = context.WaitForWindowWithDescendant("KeepShellOpenCheckBox", TimeSpan.FromSeconds(15));
        Assert.NotNull(settingsWindow);
        return settingsWindow;
    }

    /// <summary>
    ///     Reads the settings file the application wrote.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <returns>The file's text, or an empty string when it cannot be read right now (for example
    ///     while it is being replaced).</returns>
    private static string ReadSettings(AgentControlTestContext context)
    {
        try
        {
            return SharedFile.ReadAllText(Path.Combine(context.ConfigDirectory, "settings.json"));
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    ///     Finds an element by its AutomationId, waiting a bounded time for it to appear.
    /// </summary>
    /// <param name="parent">The element to search under.</param>
    /// <param name="automationId">The AutomationId to find.</param>
    /// <returns>The element.</returns>
    private static AutomationElement Find(AutomationElement parent, string automationId)
    {
        var element = Retry.WhileNull(
            () => parent.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200)).Result;
        Assert.NotNull(element);
        return element;
    }
}
