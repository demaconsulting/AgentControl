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
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Material.Icons;
using Material.Icons.Avalonia;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     Says which color the user chose in a <see cref="ColorPalettePanel"/>.
/// </summary>
/// <param name="colorName">The chosen palette color's name, or <see langword="null"/> when the user
///     cleared the choice.</param>
internal sealed class ColorChosenEventArgs(string? colorName) : EventArgs
{
    /// <summary>
    ///     Gets the chosen palette color's name, or <see langword="null"/> when the user cleared the
    ///     choice (Automatic for a card, None for a group).
    /// </summary>
    public string? ColorName { get; } = colorName;
}

/// <summary>
///     The color palette pop-up's content: a heading that names the color in effect, one swatch per
///     palette color and a button that clears the choice. Shared by a repo card's "Card color..."
///     and a group header's "Group color..." pop-ups.
/// </summary>
/// <remarks>
///     <para>
///     One control, so the card and the group palettes look and behave the same and the palette is
///     defined once (the swatches come from <see cref="CardColorPalette.All"/>, their fills from the
///     theme-aware <c>avatar{Index}</c> swatch styles). It only shows and reports a choice: the
///     owner binds <see cref="Heading"/> and <see cref="SelectedColorName"/>, and applies the color
///     <see cref="ColorChosen"/> reports.
///     </para>
///     <para>
///     Every swatch is identified for assistive technology and UI tests: its AutomationId is
///     <c>{IdPrefix}Swatch_{Name}</c>, its accessible name, tooltip and tag are the color's name, and
///     the chosen one has the <c>selected</c> class (which shows its check mark). The heading is named
///     <c>{IdPrefix}HeadingText</c> and the clear button's AutomationId is
///     <c>{IdPrefix}{ClearText}Button</c>, so the card palette keeps <c>CardColorSwatch_Teal</c>,
///     <c>CardColorHeadingText</c> and <c>CardColorAutomaticButton</c>, and the group palette gets
///     <c>GroupColorSwatch_Teal</c>, <c>GroupColorHeadingText</c> and <c>GroupColorNoneButton</c>.
///     The parts are (re)built when <see cref="IdPrefix"/>, <see cref="ClearText"/> or
///     <see cref="ClearIcon"/> change, which a template does once before the control is shown, so a
///     part's name is never changed after it was styled. Not thread-safe; UI thread only.
///     </para>
/// </remarks>
internal sealed class ColorPalettePanel : UserControl
{
    /// <summary>
    ///     Identifies the <see cref="IdPrefix"/> property.
    /// </summary>
    public static readonly StyledProperty<string> IdPrefixProperty =
        AvaloniaProperty.Register<ColorPalettePanel, string>(nameof(IdPrefix), "CardColor");

    /// <summary>
    ///     Identifies the <see cref="Heading"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> HeadingProperty =
        AvaloniaProperty.Register<ColorPalettePanel, string?>(nameof(Heading));

    /// <summary>
    ///     Identifies the <see cref="SelectedColorName"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> SelectedColorNameProperty =
        AvaloniaProperty.Register<ColorPalettePanel, string?>(nameof(SelectedColorName));

    /// <summary>
    ///     Identifies the <see cref="ClearText"/> property.
    /// </summary>
    public static readonly StyledProperty<string> ClearTextProperty =
        AvaloniaProperty.Register<ColorPalettePanel, string>(nameof(ClearText), "Automatic");

    /// <summary>
    ///     Identifies the <see cref="ClearTooltip"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> ClearTooltipProperty =
        AvaloniaProperty.Register<ColorPalettePanel, string?>(nameof(ClearTooltip));

    /// <summary>
    ///     Identifies the <see cref="ClearIcon"/> property.
    /// </summary>
    public static readonly StyledProperty<MaterialIconKind> ClearIconProperty =
        AvaloniaProperty.Register<ColorPalettePanel, MaterialIconKind>(nameof(ClearIcon), MaterialIconKind.AutoFix);

    /// <summary>
    ///     The heading that names the color in effect; rebuilt with the other parts.
    /// </summary>
    private TextBlock _heading = new();

    /// <summary>
    ///     The swatches, in palette order; rebuilt with the other parts.
    /// </summary>
    private List<Button> _swatches = [];

    /// <summary>
    ///     The check mark on the clear button, shown while no color is chosen; rebuilt with the other
    ///     parts.
    /// </summary>
    private MaterialIcon _clearCheck = new();

    /// <summary>
    ///     Initializes a new palette with the card palette's defaults (<c>CardColor</c> ids and an
    ///     <c>Automatic</c> button).
    /// </summary>
    public ColorPalettePanel()
    {
        BuildParts();
    }

    /// <summary>
    ///     Gets or sets the prefix of the parts' names and AutomationIds, for example <c>CardColor</c>
    ///     or <c>GroupColor</c>; <c>CardColor</c> by default.
    /// </summary>
    public string IdPrefix
    {
        get => GetValue(IdPrefixProperty);
        set => SetValue(IdPrefixProperty, value);
    }

    /// <summary>
    ///     Gets or sets the heading's text, which names the color in effect in words (for example
    ///     <c>Card color: Teal</c>).
    /// </summary>
    public string? Heading
    {
        get => GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    /// <summary>
    ///     Gets or sets the name of the chosen palette color (letter case ignored), or
    ///     <see langword="null"/> when none is chosen (the clear button then shows the check mark).
    /// </summary>
    public string? SelectedColorName
    {
        get => GetValue(SelectedColorNameProperty);
        set => SetValue(SelectedColorNameProperty, value);
    }

    /// <summary>
    ///     Gets or sets the text of the button that clears the choice, for example <c>Automatic</c>
    ///     or <c>None</c>; also part of its AutomationId.
    /// </summary>
    public string ClearText
    {
        get => GetValue(ClearTextProperty);
        set => SetValue(ClearTextProperty, value);
    }

    /// <summary>
    ///     Gets or sets the tooltip of the button that clears the choice, which says what "no color"
    ///     means here.
    /// </summary>
    public string? ClearTooltip
    {
        get => GetValue(ClearTooltipProperty);
        set => SetValue(ClearTooltipProperty, value);
    }

    /// <summary>
    ///     Gets or sets the icon of the button that clears the choice; <see cref="MaterialIconKind.AutoFix"/>
    ///     by default.
    /// </summary>
    public MaterialIconKind ClearIcon
    {
        get => GetValue(ClearIconProperty);
        set => SetValue(ClearIconProperty, value);
    }

    /// <summary>
    ///     Raised when the user clicks a swatch (<see cref="ColorChosenEventArgs.ColorName"/> is the
    ///     color's name) or the clear button (it is <see langword="null"/>).
    /// </summary>
    public event EventHandler<ColorChosenEventArgs>? ColorChosen;

    /// <summary>
    ///     Keeps the parts in step with the properties: the names and the clear button need a rebuild,
    ///     the heading, the tooltip and the selection are updated in place.
    /// </summary>
    /// <param name="change">The property change.</param>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IdPrefixProperty || change.Property == ClearTextProperty ||
            change.Property == ClearIconProperty || change.Property == ClearTooltipProperty)
        {
            BuildParts();
        }
        else if (change.Property == HeadingProperty)
        {
            _heading.Text = Heading;
        }
        else if (change.Property == SelectedColorNameProperty)
        {
            UpdateSelection();
        }
    }

    /// <summary>
    ///     Builds the heading, the swatches and the clear button afresh with the current names, so no
    ///     existing part's name ever changes.
    /// </summary>
    private void BuildParts()
    {
        var prefix = IdPrefix;
        _heading = new TextBlock { Name = prefix + "HeadingText", Text = Heading, FontWeight = FontWeight.SemiBold };

        // One swatch per palette color, five to a row, filled by the theme-aware swatch styles
        var grid = new UniformGrid { Columns = 5 };
        _swatches = [];
        foreach (var color in CardColorPalette.All)
        {
            var swatch = new Button
            {
                Margin = new Thickness(3),
                Tag = color.Name,
                Content = new MaterialIcon { Kind = MaterialIconKind.Check }
            };
            swatch.Classes.Add("swatch");
            swatch.Classes.Add($"avatar{color.Index}");
            AutomationProperties.SetAutomationId(swatch, $"{prefix}Swatch_{color.Name}");
            AutomationProperties.SetName(swatch, color.Name);
            ToolTip.SetTip(swatch, color.Name);
            swatch.Click += (_, _) => ColorChosen?.Invoke(this, new ColorChosenEventArgs(color.Name));
            _swatches.Add(swatch);
            grid.Children.Add(swatch);
        }

        // The clear button: its own icon and text, and a check mark while no color is chosen
        _clearCheck = new MaterialIcon { Kind = MaterialIconKind.Check, Width = 16, Height = 16 };
        var clear = new Button
        {
            Name = prefix + ClearText + "Button",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Tag = string.Empty,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new MaterialIcon { Kind = ClearIcon, Width = 16, Height = 16 },
                    new TextBlock { Text = ClearText, VerticalAlignment = VerticalAlignment.Center },
                    _clearCheck
                }
            }
        };
        clear.Classes.Add("actionButton");
        clear.Classes.Add("secondary");
        AutomationProperties.SetAutomationId(clear, prefix + ClearText + "Button");
        ToolTip.SetTip(clear, ClearTooltip);
        clear.Click += (_, _) => ColorChosen?.Invoke(this, new ColorChosenEventArgs(null));

        Content = new StackPanel { Spacing = 10, Children = { _heading, grid, clear } };
        UpdateSelection();
    }

    /// <summary>
    ///     Marks the chosen swatch (and only it), or the clear button when no color is chosen.
    /// </summary>
    private void UpdateSelection()
    {
        var chosen = CardColorPalette.Find(SelectedColorName)?.Name;
        foreach (var swatch in _swatches)
        {
            swatch.Classes.Set("selected", string.Equals((string?)swatch.Tag, chosen, StringComparison.Ordinal));
        }

        _clearCheck.IsVisible = chosen is null;
    }
}
