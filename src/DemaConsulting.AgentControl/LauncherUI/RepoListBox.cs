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

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     The list of the main window's section headers and repo cards: a multiple-selection
///     <see cref="ListBox"/> whose selection behaves the way a file manager's does, so several cards can
///     be selected, dragged together and removed together.
/// </summary>
/// <remarks>
///     It differs from the plain list in three ways. A plain press on a card that is already one of several
///     selected cards keeps the selection - the list would otherwise reduce it to that card before a drag
///     could start - and reduces it to the card only when the button is released without a drag (see
///     <see cref="CancelDeferredSelection"/>). <c>Ctrl+A</c> selects the cards only, never the section
///     headers. The <c>Delete</c> key asks the window to remove the selected cards
///     (<see cref="DeleteRequested"/>); a key pressed in a text box is left to the text box. The list keeps
///     the look and the styles of <see cref="ListBox"/>. Used on the UI thread only.
/// </remarks>
internal sealed class RepoListBox : ListBox
{
    /// <summary>
    ///     The selected row that was pressed with the plain left button and whose selection by itself was
    ///     put off to the release; <see langword="null"/> when no press is waiting.
    /// </summary>
    private ListBoxItem? _deferredRow;

    /// <summary>
    ///     Gets the style key: the list is styled and templated as a <see cref="ListBox"/>.
    /// </summary>
    protected override Type StyleKeyOverride => typeof(ListBox);

    /// <summary>
    ///     Raised when the user presses <c>Delete</c> in the list while at least one row is selected; the
    ///     window decides what to do (it asks to remove the selected cards).
    /// </summary>
    public event EventHandler? DeleteRequested;

    /// <summary>
    ///     Forgets a press that waits for its release to reduce the selection, because the press became a
    ///     drag of the selected cards and the selection is to stay as it is.
    /// </summary>
    public void CancelDeferredSelection() => _deferredRow = null;

    /// <summary>
    ///     Decides whether a pointer event selects a row, keeping a selection of several cards when one of
    ///     them is pressed so it can be dragged.
    /// </summary>
    /// <param name="selectable">The row the event is for.</param>
    /// <param name="eventArgs">The event.</param>
    /// <returns><see langword="true"/> when the event selects the row.</returns>
    protected override bool ShouldTriggerSelection(Visual selectable, PointerEventArgs eventArgs)
    {
        if (selectable is ListBoxItem { IsSelected: true } row &&
            eventArgs.Pointer.Type == PointerType.Mouse &&
            SelectedItems?.Count > 1 &&
            eventArgs.Properties.PointerUpdateKind is PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased &&
            !ItemSelectionEventTriggers.HasToggleSelectionModifier(row, eventArgs) &&
            !ItemSelectionEventTriggers.HasRangeSelectionModifier(row, eventArgs))
        {
            if (eventArgs.RoutedEvent == PointerPressedEvent)
            {
                // Put off: the selection is reduced to this card only if the button is released without a drag
                _deferredRow = row;
                return false;
            }

            var waiting = ReferenceEquals(_deferredRow, row);
            _deferredRow = null;
            return waiting && new Rect(row.Bounds.Size).Contains(eventArgs.GetPosition(row));
        }

        if (eventArgs.RoutedEvent == PointerPressedEvent)
        {
            _deferredRow = null;
        }

        return base.ShouldTriggerSelection(selectable, eventArgs);
    }

    /// <summary>
    ///     Handles <c>Ctrl+A</c> (select every card) and <c>Delete</c> (ask to remove the selected cards),
    ///     leaving every other key, and every key pressed in a text box, to the list.
    /// </summary>
    /// <param name="e">The key.</param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Source is not TextBox && !e.Handled)
        {
            if (e.Key == Key.A && e.KeyModifiers == KeyModifiers.Control)
            {
                SelectAllCards();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None && SelectedItems?.Count > 0)
            {
                DeleteRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
    }

    /// <summary>
    ///     Selects every repo card in the list and nothing else: the section headers are not part of a
    ///     selection.
    /// </summary>
    public void SelectAllCards()
    {
        Selection.BeginBatchUpdate();
        try
        {
            Selection.Clear();
            for (var index = 0; index < Items.Count; index++)
            {
                if (Items[index] is RepoCardViewModel)
                {
                    Selection.Select(index);
                }
            }
        }
        finally
        {
            Selection.EndBatchUpdate();
        }
    }
}
