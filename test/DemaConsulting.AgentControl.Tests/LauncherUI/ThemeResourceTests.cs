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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Headless tests of the application theme: how the user's choice maps to an Avalonia theme
///     variant and is applied to the running application, and that the shared style resources
///     (<c>AppStyles.axaml</c>) exist for both the light and the dark variant with text colors that
///     meet WCAG AA contrast against the surfaces they are drawn on.
/// </summary>
public class ThemeResourceTests
{
    /// <summary>
    ///     The minimum contrast ratio for normal text (WCAG AA).
    /// </summary>
    private const double TextContrast = 4.5;

    /// <summary>
    ///     The minimum contrast ratio for graphical objects such as status bars and icons (WCAG AA).
    /// </summary>
    private const double GraphicContrast = 3.0;

    /// <summary>
    ///     The number of avatar palette entries the style resources must define; the view model's
    ///     <see cref="RepoCardViewModel.AvatarPaletteSize"/> chooses among them.
    /// </summary>
    private static readonly int AvatarCount = RepoCardViewModel.AvatarPaletteSize;

    /// <summary>
    ///     Test that each theme choice maps to the matching Avalonia theme variant, the system
    ///     default being <see cref="ThemeVariant.Default"/> (which follows the operating system).
    /// </summary>
    /// <param name="preferenceName">The name of the user's choice.</param>
    /// <param name="variantName">The expected variant's name.</param>
    [Theory]
    [InlineData("System", "Default")]
    [InlineData("Light", "Light")]
    [InlineData("Dark", "Dark")]
    public void App_ToThemeVariant_MapsEachPreferenceToItsVariant(string preferenceName, string variantName)
    {
        // Act
        var variant = App.ToThemeVariant(Enum.Parse<ThemePreference>(preferenceName));

        // Assert
        Assert.Equal(variantName, variant.Key.ToString());
        Assert.Equal(ThemeVariant.Default, App.ToThemeVariant((ThemePreference)99));
    }

    /// <summary>
    ///     Test that applying a choice switches the running application's theme at once, and that
    ///     the system default hands control back to the operating system.
    /// </summary>
    [Fact]
    public void App_ApplyTheme_SwitchesRunningApplicationTheme() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var application = Application.Current!;
        var original = application.RequestedThemeVariant;
        try
        {
            // Act / Assert: light, then dark, then the system default
            App.ApplyTheme(ThemePreference.Light);
            Assert.Equal(ThemeVariant.Light, application.RequestedThemeVariant);
            Assert.Equal(ThemeVariant.Light, application.ActualThemeVariant);

            App.ApplyTheme(ThemePreference.Dark);
            Assert.Equal(ThemeVariant.Dark, application.RequestedThemeVariant);
            Assert.Equal(ThemeVariant.Dark, application.ActualThemeVariant);

            App.ApplyTheme(ThemePreference.System);
            Assert.Equal(ThemeVariant.Default, application.RequestedThemeVariant);
        }
        finally
        {
            application.RequestedThemeVariant = original;
        }
    });

    /// <summary>
    ///     Test that the light and the dark dictionary define exactly the same resource keys, so no
    ///     control can be left without a color in one theme.
    /// </summary>
    [Fact]
    public void AppStyles_LightAndDarkDictionaries_DefineTheSameKeys() => HeadlessTestApp.Run(() =>
    {
        // Arrange / Act
        var light = LoadPalette(ThemeVariant.Light);
        var dark = LoadPalette(ThemeVariant.Dark);

        // Assert
        Assert.NotEmpty(light.Keys);
        Assert.Equal(light.Keys.Order(), dark.Keys.Order());
    });

    /// <summary>
    ///     Test that every text color of the shared styles has at least AA contrast against the
    ///     surface it is drawn on, in both themes: chips, secondary and disabled buttons, ghost icon
    ///     buttons, validation, warning and success text, the empty-state tile and every avatar
    ///     palette entry; and that status accents and the favorite star reach graphics contrast.
    /// </summary>
    /// <param name="variantName">The theme variant to check.</param>
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void AppStyles_TextAndGraphicColors_MeetContrastInBothThemes(string variantName) => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var palette = LoadPalette(variantName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark);
        var card = Opaque(palette, "RepoCardBackground");
        var page = Opaque(palette, "PageBackground");
        var failures = new List<string>();

        void CheckText(string label, Color foreground, Color background, double minimum = TextContrast)
        {
            var ratio = Contrast(foreground, background);
            if (ratio < minimum)
            {
                failures.Add($"{label}: {ratio:F2} < {minimum}");
            }
        }

        // Act / Assert: each chip's text over its tinted fill over the card
        foreach (var chip in new[] { "Neutral", "Grey", "Package", "Amber", "Red" })
        {
            CheckText(
                $"Chip{chip}", Opaque(palette, $"Chip{chip}Foreground"),
                Over(Brush(palette, $"Chip{chip}Background"), card));
        }

        CheckText("ChipAmber hover", Opaque(palette, "ChipAmberForeground"), Over(Brush(palette, "ChipAmberBackgroundHover"), card));
        CheckText("ChipAmber pressed", Opaque(palette, "ChipAmberForeground"), Over(Brush(palette, "ChipAmberBackgroundPressed"), card));
        CheckText("ChipGrey hover", Opaque(palette, "ChipGreyForeground"), Over(Brush(palette, "ChipGreyBackgroundHover"), card));

        // Secondary button, every state
        foreach (var state in new[] { string.Empty, "Hover", "Pressed", "Disabled" })
        {
            CheckText(
                $"ButtonSecondary{state}",
                Opaque(palette, state == "Disabled" ? "ButtonSecondaryForegroundDisabled" : "ButtonSecondaryForeground"),
                Over(Brush(palette, $"ButtonSecondaryBackground{state}"), card));
        }

        CheckText(
            "ButtonPrimary disabled", Opaque(palette, "ButtonPrimaryForegroundDisabled"),
            Opaque(palette, "ButtonPrimaryBackgroundDisabled"));

        // Ghost icon buttons: normal on the card, hover and pressed on their tinted background
        CheckText("IconGhost", Opaque(palette, "IconGhostForeground"), card);
        CheckText("IconGhost hover", Opaque(palette, "IconGhostForegroundHover"), Over(Brush(palette, "IconGhostBackgroundHover"), card));
        CheckText("IconGhost pressed", Opaque(palette, "IconGhostForegroundHover"), Over(Brush(palette, "IconGhostBackgroundPressed"), card));

        // Validation, caution and success text on the page and on a card
        foreach (var semantic in new[] { "ErrorForeground", "WarningForeground", "SuccessForeground" })
        {
            CheckText($"{semantic} on page", Opaque(palette, semantic), page);
            CheckText($"{semantic} on card", Opaque(palette, semantic), card);
        }

        // The text of a group header and its chevron, drawn straight on the page
        CheckText("GroupHeaderForeground on page", Opaque(palette, "GroupHeaderForeground"), page);
        CheckText("Group chevron on page", Opaque(palette, "MenuGroupHeaderForeground"), page);

        // The empty-state icon tile
        CheckText(
            "EmptyStateTile", Opaque(palette, "EmptyStateTileForeground"),
            Over(Brush(palette, "EmptyStateTileBackground"), card), GraphicContrast);

        // Every avatar letter on its tile
        for (var index = 0; index < AvatarCount; index++)
        {
            CheckText(
                $"Avatar{index}", Opaque(palette, $"AvatarForeground{index}"), Opaque(palette, $"AvatarBackground{index}"));
        }

        // Graphics: status accents and the favorite star against the card
        foreach (var graphic in new[]
                 {
                     "StatusAccentUpToDate", "StatusAccentUpgrade", "StatusAccentProblem",
                     "StatusAccentNeutral", "FavoriteStarForeground"
                 })
        {
            CheckText(graphic, Opaque(palette, graphic), card, GraphicContrast);
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    });

    /// <summary>
    ///     Test that the letters (and the check mark of a chosen swatch) on every tile color are the
    ///     better of white and near-black for that fill in each theme, and meet AA contrast: so
    ///     whichever palette color the user chooses, the initials stay readable in both themes.
    /// </summary>
    /// <param name="variantName">The theme variant to check.</param>
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void AppStyles_TileLetters_AreTheBetterOfLightAndDarkTextOnEveryPaletteColor(string variantName) =>
        HeadlessTestApp.Run(() =>
        {
            // Arrange
            var palette = LoadPalette(variantName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark);
            var white = Colors.White;
            var nearBlack = Color.Parse("#111114");
            var problems = new List<string>();

            // Act: every color a user can choose, by its palette index
            foreach (var color in CardColorPalette.All)
            {
                var fill = Opaque(palette, $"AvatarBackground{color.Index}");
                var letters = Opaque(palette, $"AvatarForeground{color.Index}");
                var chosen = Contrast(letters, fill);
                var best = Math.Max(Contrast(white, fill), Contrast(nearBlack, fill));
                if (chosen < TextContrast || chosen + 0.001 < best)
                {
                    problems.Add($"{color.Name}: {chosen:F2} (best available {best:F2})");
                }
            }

            // Assert
            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        });

    /// <summary>
    ///     Loads the theme dictionary of one variant from the shared styles file.
    /// </summary>
    /// <param name="variant">The variant (<see cref="ThemeVariant.Light"/> or
    ///     <see cref="ThemeVariant.Dark"/>).</param>
    /// <returns>The variant's resources by key.</returns>
    private static Dictionary<object, object?> LoadPalette(ThemeVariant variant)
    {
        var styles = (Styles)AvaloniaXamlLoader.Load(
            // cspell:disable-next-line
            new Uri("avares://DemaConsulting.AgentControl/LauncherUI/AppStyles.axaml"));
        var resources = (ResourceDictionary)styles.Resources;
        var dictionary = (ResourceDictionary)resources.ThemeDictionaries[variant];
        // TryGetResource materializes each value (the loader stores brushes as deferred content)
        return dictionary.Keys.ToDictionary(
            key => key, key => dictionary.TryGetResource(key, variant, out var value) ? value : null);
    }

    /// <summary>
    ///     Gets a brush resource.
    /// </summary>
    /// <param name="palette">The theme's resources.</param>
    /// <param name="key">The resource key.</param>
    /// <returns>The brush.</returns>
    private static SolidColorBrush Brush(Dictionary<object, object?> palette, string key) =>
        Assert.IsType<SolidColorBrush>(palette[key]);

    /// <summary>
    ///     Gets a brush resource's color, which must be fully opaque.
    /// </summary>
    /// <param name="palette">The theme's resources.</param>
    /// <param name="key">The resource key.</param>
    /// <returns>The opaque color.</returns>
    private static Color Opaque(Dictionary<object, object?> palette, string key)
    {
        var color = Brush(palette, key).Color;
        Assert.True(color.A == 255, $"{key} must be opaque");
        return color;
    }

    /// <summary>
    ///     Composites a (possibly translucent) brush over an opaque background.
    /// </summary>
    /// <param name="brush">The translucent fill.</param>
    /// <param name="background">The opaque color beneath it.</param>
    /// <returns>The resulting opaque color.</returns>
    private static Color Over(SolidColorBrush brush, Color background)
    {
        var alpha = brush.Color.A / 255.0;
        byte Mix(byte top, byte bottom) => (byte)Math.Round((alpha * top) + ((1 - alpha) * bottom));
        return Color.FromRgb(
            Mix(brush.Color.R, background.R), Mix(brush.Color.G, background.G), Mix(brush.Color.B, background.B));
    }

    /// <summary>
    ///     Computes the WCAG contrast ratio of two opaque colors.
    /// </summary>
    /// <param name="first">One color.</param>
    /// <param name="second">The other color.</param>
    /// <returns>The ratio, from 1 (identical) to 21 (black on white).</returns>
    private static double Contrast(Color first, Color second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>
    ///     Computes the WCAG relative luminance of a color.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <returns>The luminance from 0 (black) to 1 (white).</returns>
    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var normalized = value / 255.0;
            return normalized <= 0.03928 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }
}
