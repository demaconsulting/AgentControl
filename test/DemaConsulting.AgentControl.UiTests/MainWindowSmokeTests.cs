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

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     Smoke test verifying AgentControl's main window launches and is visible, per
///     architecture.md's FlaUI end-to-end testability requirement.
/// </summary>
public sealed class MainWindowSmokeTests
{
    /// <summary>
    ///     Launches AgentControl against an isolated config directory and verifies the main
    ///     window appears, is visible, and exposes the expected title/AutomationId.
    /// </summary>
    [Fact]
    public void MainWindow_Launch_WindowVisibleWithExpectedAutomationId()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        using var context = new AgentControlTestContext();

        // Act
        var mainWindow = context.Launch();

        // Assert
        Assert.False(mainWindow.IsOffscreen);
        Assert.Equal("AgentControl", mainWindow.Title);
        Assert.Equal("MainWindowRoot", mainWindow.AutomationId);
    }
}
