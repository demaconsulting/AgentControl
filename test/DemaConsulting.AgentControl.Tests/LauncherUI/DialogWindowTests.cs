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

using Avalonia.Controls;
using Avalonia.Interactivity;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Headless tests for the small dialog windows' code-behind (<see cref="MessageBoxWindow"/>,
///     <see cref="ConfirmationWindow"/>, <see cref="AboutWindow"/>, <see cref="SettingsWindow"/>).
/// </summary>
public class DialogWindowTests
{
    /// <summary>
    ///     Raises a <see cref="Button.ClickEvent"/> on the named button, as a user click would.
    /// </summary>
    /// <param name="window">The window containing the button.</param>
    /// <param name="name">The button's XAML name.</param>
    internal static void Click(Window window, string name)
    {
        var button = window.FindControl<Button>(name);
        Assert.NotNull(button);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    /// <summary>
    ///     Test that the message box shows its message and closes when OK is clicked.
    /// </summary>
    [Fact]
    public void MessageBoxWindow_OkClicked_ShowsMessageAndCloses() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var window = new MessageBoxWindow("Something failed");
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();

        // Act
        Click(window, "MessageBoxOkButton");

        // Assert
        Assert.Equal("Something failed", window.FindControl<TextBlock>("MessageText")!.Text);
        Assert.True(closed);
    });

    /// <summary>
    ///     Test that the message box constructors reject a null message and support the
    ///     parameterless previewer constructor.
    /// </summary>
    [Fact]
    public void MessageBoxWindow_Constructors_NullMessageThrows_ParameterlessWorks() => HeadlessTestApp.Run(() =>
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new MessageBoxWindow(null!));
        Assert.NotNull(new MessageBoxWindow());
    });

    /// <summary>
    ///     Test that Yes closes the confirmation dialog with a true result.
    /// </summary>
    [Fact]
    public void ConfirmationWindow_YesClicked_ReturnsTrue() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var owner = new Window();
        owner.Show();
        var dialog = new ConfirmationWindow("Are you sure?");
        var result = dialog.ShowDialog<bool>(owner);

        // Act
        Click(dialog, "ConfirmationYesButton");

        // Assert
        Assert.True(await result);
        Assert.Equal("Are you sure?", dialog.FindControl<TextBlock>("ConfirmationMessageText")!.Text);
    });

    /// <summary>
    ///     Test that No closes the confirmation dialog with a false result.
    /// </summary>
    [Fact]
    public void ConfirmationWindow_NoClicked_ReturnsFalse() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var owner = new Window();
        owner.Show();
        var dialog = new ConfirmationWindow("Are you sure?");
        var result = dialog.ShowDialog<bool>(owner);

        // Act
        Click(dialog, "ConfirmationNoButton");

        // Assert
        Assert.False(await result);
    });

    /// <summary>
    ///     Test that the confirmation window rejects a null message and supports the
    ///     parameterless previewer constructor.
    /// </summary>
    [Fact]
    public void ConfirmationWindow_Constructors_NullMessageThrows_ParameterlessWorks() => HeadlessTestApp.Run(() =>
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new ConfirmationWindow(null!));
        Assert.NotNull(new ConfirmationWindow());
    });

    /// <summary>
    ///     Test that the About window shows its text and dependency list, and closes on Close.
    /// </summary>
    [Fact]
    public void AboutWindow_Constructed_ShowsInfoAndClosesOnCloseClick() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var window = new AboutWindow();
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();

        // Assert: static text and dependency list are populated
        Assert.Contains("AgentControl", window.FindControl<TextBlock>("AboutVersionText")!.Text);
        Assert.Equal("Copyright (c) DEMA Consulting", window.FindControl<TextBlock>("AboutCopyrightText")!.Text);
        Assert.Contains("MIT", window.FindControl<TextBlock>("AboutLicenseText")!.Text);
        Assert.NotEmpty(window.FindControl<TextBlock>("AboutTaglineText")!.Text!);
        Assert.NotNull(window.FindControl<ItemsControl>("AboutDependenciesList")!.ItemsSource);

        // Act
        Click(window, "AboutCloseButton");

        // Assert
        Assert.True(closed);
    });

    /// <summary>
    ///     Test that saving through the bound view model closes the settings window.
    /// </summary>
    [Fact]
    public void SettingsWindow_SavedRaised_ClosesWindow() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        AppSettings? saved = null;
        var viewModel = new SettingsWindowViewModel(new AppSettings(), updated => saved = updated);
        var window = new SettingsWindow(viewModel);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();

        // Act
        viewModel.SaveCommand.Execute(null);

        // Assert
        Assert.NotNull(saved);
        Assert.True(closed);
    });

    /// <summary>
    ///     Test that the settings window rejects a null view model and that the browse button is
    ///     a no-op without a bound view model.
    /// </summary>
    [Fact]
    public void SettingsWindow_NullViewModel_ThrowsAndBrowseWithoutViewModelIsNoOp() => HeadlessTestApp.Run(() =>
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new SettingsWindow(null!));

        var window = new SettingsWindow();
        window.Show();
        Click(window, "BrowsePackageSourceButton");
        Assert.Null(window.DataContext);
    });
}
