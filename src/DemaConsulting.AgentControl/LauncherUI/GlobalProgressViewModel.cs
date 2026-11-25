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

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     How the strip's Cancel button describes what it stops, for screen readers and the tooltip.
/// </summary>
/// <remarks>
///     Exists because the one Cancel button serves every cancelable operation (adding repositories,
///     cloning a repository): a button that always said "Cancel adding repositories" would tell a
///     screen-reader user, and anyone who hovers it, the wrong thing while a clone runs.
/// </remarks>
/// <param name="AccessibleName">The name screen readers announce for the Cancel button, for example
///     <c>Cancel adding repositories</c>. Must not be blank.</param>
/// <param name="ToolTip">The tooltip of the Cancel button, saying what stopping does. Must not be
///     blank.</param>
internal sealed record GlobalProgressCancelDescription(string AccessibleName, string ToolTip)
{
    /// <summary>
    ///     Gets the description of an operation that adds repositories, which every operation that
    ///     gives no description of its own gets.
    /// </summary>
    public static GlobalProgressCancelDescription AddingRepositories { get; } = new(
        "Cancel adding repositories",
        "Stop adding repositories; nothing more is added");
}

/// <summary>
///     View model of the main window's global progress strip: the thin bar, the short status text and
///     the Cancel button shown directly under the app bar while a bulk operation (adding several
///     repositories, reading the cards at start-up, refreshing every card after Settings were saved)
///     runs.
/// </summary>
/// <remarks>
///     <para>
///     Bulk operations are not tied to one card, so their progress needs a place of its own that never
///     covers the list and never moves it while idle: the strip is fully collapsed when
///     <see cref="IsActive"/> is <see langword="false"/>. An operation starts with
///     <see cref="Begin"/>, which returns the <see cref="GlobalProgressOperation"/> its owner reports
///     through (indeterminate until a count is known, then determinate) and disposes when it ends.
///     </para>
///     <para>
///     Operations may overlap (an add while the cards refresh after a Settings save): the strip shows
///     the most recently begun running operation that can still be canceled or, when none can, the
///     most recently begun running operation. That way an add's Cancel stays offered while the cards
///     refresh after a Settings save, and the refresh is shown once the add has ended or was canceled;
///     with none running the strip is idle. Not thread-safe: like every view model here
///     it is created and changed on the UI thread only (operations report from the continuations of
///     their background work, which resume on the UI thread).
///     </para>
/// </remarks>
internal sealed class GlobalProgressViewModel : ViewModelBase
{
    /// <summary>
    ///     The operations that are running, oldest first; see <see cref="Shown"/> for the one shown.
    /// </summary>
    private readonly List<GlobalProgressOperation> _operations = [];

    /// <summary>Backing field for <see cref="IsActive"/>.</summary>
    private bool _isActive;

    /// <summary>Backing field for <see cref="Text"/>.</summary>
    private string _text = string.Empty;

    /// <summary>Backing field for <see cref="IsIndeterminate"/>.</summary>
    private bool _isIndeterminate = true;

    /// <summary>Backing field for <see cref="Value"/>.</summary>
    private double _value;

    /// <summary>Backing field for <see cref="Maximum"/>.</summary>
    private double _maximum = 1;

    /// <summary>Backing field for <see cref="CanCancel"/>.</summary>
    private bool _canCancel;

    /// <summary>Backing field for <see cref="CancelDescription"/>.</summary>
    private GlobalProgressCancelDescription _cancelDescription = GlobalProgressCancelDescription.AddingRepositories;

    /// <summary>
    ///     Initializes an idle progress strip.
    /// </summary>
    public GlobalProgressViewModel()
    {
        CancelCommand = new RelayCommand(CancelShown, () => CanCancel);
    }

    /// <summary>
    ///     Gets a value indicating whether an operation is running, so the strip is shown; when
    ///     <see langword="false"/> the strip takes no space at all.
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        private set => SetField(ref _isActive, value);
    }

    /// <summary>
    ///     Gets the short status text of the shown operation (for example
    ///     <c>Adding 7 of 20 repositories...</c>); empty when idle.
    /// </summary>
    public string Text
    {
        get => _text;
        private set
        {
            if (SetField(ref _text, value))
            {
                OnPropertyChanged(nameof(AccessibleName));
            }
        }
    }

    /// <summary>
    ///     Gets the name screen readers announce for the progress bar: the status text, so the bar is
    ///     never announced as an unnamed progress bar.
    /// </summary>
    public string AccessibleName => Text;

    /// <summary>
    ///     Gets a value indicating whether the bar is indeterminate (the shown operation does not know
    ///     how much work there is yet, or is in a step it cannot count, such as saving).
    /// </summary>
    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set => SetField(ref _isIndeterminate, value);
    }

    /// <summary>
    ///     Gets how much of the shown operation is done, between 0 and <see cref="Maximum"/>; 0 while
    ///     <see cref="IsIndeterminate"/>.
    /// </summary>
    public double Value
    {
        get => _value;
        private set => SetField(ref _value, value);
    }

    /// <summary>
    ///     Gets the amount of work of the shown operation (at least 1, so an empty batch never divides
    ///     by zero in the bar).
    /// </summary>
    public double Maximum
    {
        get => _maximum;
        private set => SetField(ref _maximum, value);
    }

    /// <summary>
    ///     Gets a value indicating whether the shown operation can be canceled now, so the strip shows
    ///     its Cancel button.
    /// </summary>
    public bool CanCancel
    {
        get => _canCancel;
        private set
        {
            if (SetField(ref _canCancel, value))
            {
                CancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    ///     Gets the description of what the Cancel button stops, taken from the shown operation (the
    ///     adding-repositories description when none is shown or the operation gave none).
    /// </summary>
    private GlobalProgressCancelDescription CancelDescription
    {
        get => _cancelDescription;
        set
        {
            if (!ReferenceEquals(_cancelDescription, value))
            {
                _cancelDescription = value;
                OnPropertyChanged(nameof(CancelAccessibleName));
                OnPropertyChanged(nameof(CancelToolTip));
            }
        }
    }

    /// <summary>
    ///     Gets the name screen readers announce for the Cancel button: it names the shown operation
    ///     (for example <c>Cancel cloning repository</c>) rather than always the add.
    /// </summary>
    public string CancelAccessibleName => CancelDescription.AccessibleName;

    /// <summary>
    ///     Gets the tooltip of the Cancel button: what stopping the shown operation does.
    /// </summary>
    public string CancelToolTip => CancelDescription.ToolTip;

    /// <summary>
    ///     Gets the command behind the strip's Cancel button: asks the shown operation to stop. Enabled
    ///     only while <see cref="CanCancel"/>; the operation's cancel action runs at most once.
    /// </summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>
    ///     Starts showing an operation: indeterminate, with the given text, until the operation reports
    ///     a count.
    /// </summary>
    /// <param name="text">The status text to show, for example <c>Adding repositories...</c>. Must
    ///     not be <see langword="null"/>.</param>
    /// <param name="cancel">What the strip's Cancel button does for this operation, or
    ///     <see langword="null"/> for an operation that cannot be canceled (no Cancel button). It runs
    ///     on the UI thread, at most once.</param>
    /// <param name="cancelDescription">How the Cancel button is named while this operation is shown,
    ///     or <see langword="null"/> for <see cref="GlobalProgressCancelDescription.AddingRepositories"/>.
    ///     Ignored without a cancel action.</param>
    /// <returns>The operation; its owner reports through it and disposes it when the work has ended
    ///     (however it ended), which removes it from the strip. It is shown at once unless an older
    ///     operation that can still be canceled is running (see <see cref="Shown"/>).</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is
    ///     <see langword="null"/>.</exception>
    public GlobalProgressOperation Begin(
        string text, Action? cancel = null, GlobalProgressCancelDescription? cancelDescription = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var operation = new GlobalProgressOperation(this, text, cancel, cancelDescription);
        _operations.Add(operation);
        Publish();
        return operation;
    }

    /// <summary>
    ///     Shows the strip's state again after an operation changed.
    /// </summary>
    /// <remarks>
    ///     Always republishes: a cancel of the shown operation can change which operation is shown. A
    ///     change of an operation that is not shown changes nothing visible (the bound properties
    ///     announce real changes only).
    /// </remarks>
    /// <param name="operation">The operation that changed.</param>
    internal void OnOperationChanged(GlobalProgressOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Publish();
    }

    /// <summary>
    ///     Removes an operation that has ended, showing the one that should be shown next (see
    ///     <see cref="Shown"/>) or going idle.
    /// </summary>
    /// <param name="operation">The operation that ended.</param>
    internal void End(GlobalProgressOperation operation)
    {
        if (_operations.Remove(operation))
        {
            Publish();
        }
    }

    /// <summary>
    ///     Gets the operation the strip shows: the most recently begun running operation that can still
    ///     be canceled or, when none can, the most recently begun running operation;
    ///     <see langword="null"/> when none is running.
    /// </summary>
    private GlobalProgressOperation? Shown
    {
        get
        {
            for (var index = _operations.Count - 1; index >= 0; index--)
            {
                if (_operations[index].IsCancelable)
                {
                    return _operations[index];
                }
            }

            return _operations.Count > 0 ? _operations[^1] : null;
        }
    }

    /// <summary>
    ///     Copies the shown operation's state (see <see cref="Shown"/>) to the bound properties, or clears
    ///     them when none is running.
    /// </summary>
    private void Publish()
    {
        var shown = Shown;
        if (shown is null)
        {
            IsActive = false;
            Text = string.Empty;
            IsIndeterminate = true;
            Value = 0;
            Maximum = 1;
            CanCancel = false;
            CancelDescription = GlobalProgressCancelDescription.AddingRepositories;
            return;
        }

        Text = shown.Text;
        IsIndeterminate = shown.IsIndeterminate;
        Maximum = Math.Max(1, shown.Total);
        Value = shown.IsIndeterminate ? 0 : Math.Clamp(shown.Completed, 0, Maximum);
        CanCancel = shown.CanCancel;
        CancelDescription = shown.CancelDescription;
        IsActive = true;
    }

    /// <summary>
    ///     Runs the shown operation's cancel action (the strip's Cancel button).
    /// </summary>
    private void CancelShown() => Shown?.RequestCancel();
}

/// <summary>
///     One bulk operation shown by the <see cref="GlobalProgressViewModel"/>: its owner reports the
///     operation's progress through it and disposes it when the work has ended.
/// </summary>
/// <remarks>
///     Created only by <see cref="GlobalProgressViewModel.Begin"/>. Not thread-safe: report from the UI
///     thread. Disposing is idempotent; reports after disposal are ignored.
/// </remarks>
internal sealed class GlobalProgressOperation : IDisposable
{
    /// <summary>
    ///     The strip that shows this operation.
    /// </summary>
    private readonly GlobalProgressViewModel _owner;

    /// <summary>
    ///     What the strip's Cancel button does for this operation, or <see langword="null"/> when it
    ///     cannot be canceled.
    /// </summary>
    private readonly Action? _cancel;

    /// <summary>
    ///     Whether the cancel action has run (it runs at most once).
    /// </summary>
    private bool _cancelRequested;

    /// <summary>
    ///     Whether the operation has ended (was disposed).
    /// </summary>
    private bool _ended;

    /// <summary>
    ///     Whether the operation currently accepts a cancel (see <see cref="AllowCancel"/>).
    /// </summary>
    private bool _allowCancel = true;

    /// <summary>
    ///     Initializes an operation; see <see cref="GlobalProgressViewModel.Begin"/>.
    /// </summary>
    /// <param name="owner">The strip that shows it.</param>
    /// <param name="text">The initial status text.</param>
    /// <param name="cancel">The cancel action, or <see langword="null"/> for none.</param>
    /// <param name="cancelDescription">How the Cancel button is named while this operation is shown, or
    ///     <see langword="null"/> for <see cref="GlobalProgressCancelDescription.AddingRepositories"/>.</param>
    internal GlobalProgressOperation(
        GlobalProgressViewModel owner, string text, Action? cancel, GlobalProgressCancelDescription? cancelDescription)
    {
        _owner = owner;
        _cancel = cancel;
        CancelDescription = cancelDescription ?? GlobalProgressCancelDescription.AddingRepositories;
        Text = text;
    }

    /// <summary>
    ///     Gets how the strip's Cancel button is named while this operation is shown.
    /// </summary>
    public GlobalProgressCancelDescription CancelDescription { get; }

    /// <summary>
    ///     Gets the operation's status text.
    /// </summary>
    public string Text { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether the operation's amount of work is unknown right now.
    /// </summary>
    public bool IsIndeterminate { get; private set; } = true;

    /// <summary>
    ///     Gets how many items of work are done (meaningful only when not <see cref="IsIndeterminate"/>).
    /// </summary>
    public int Completed { get; private set; }

    /// <summary>
    ///     Gets how many items of work there are (meaningful only when not <see cref="IsIndeterminate"/>).
    /// </summary>
    public int Total { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether the strip offers Cancel for this operation now: it has a
    ///     cancel action, accepts a cancel at this point, was not canceled already and has not ended.
    /// </summary>
    public bool CanCancel => _cancel is not null && _allowCancel && !_cancelRequested && !_ended;

    /// <summary>
    ///     Gets a value indicating whether the operation has a cancel action that was not run yet and
    ///     has not ended, whether or not it accepts a cancel at this point; such an operation is shown
    ///     in preference to newer ones (see <see cref="GlobalProgressViewModel"/>), so an add in a step
    ///     that cannot be canceled (saving) stays shown, without Cancel, rather than flipping to a newer
    ///     operation.
    /// </summary>
    internal bool IsCancelable => _cancel is not null && !_cancelRequested && !_ended;

    /// <summary>
    ///     Gets or sets whether the operation accepts a cancel at this point; an owner turns it off for
    ///     a step that must run to its end once started (saving) and back on afterwards. Has no effect
    ///     on an operation without a cancel action.
    /// </summary>
    public bool AllowCancel
    {
        get => _allowCancel;
        set
        {
            if (_allowCancel != value)
            {
                _allowCancel = value;
                Changed();
            }
        }
    }

    /// <summary>
    ///     Reports a known amount of work: the bar becomes determinate.
    /// </summary>
    /// <param name="completed">How many items are done; clamped to 0 up to <paramref name="total"/>.</param>
    /// <param name="total">How many items there are; a negative value counts as 0.</param>
    /// <param name="text">The status text to show, for example <c>Adding 7 of 20 repositories...</c>.
    ///     Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is
    ///     <see langword="null"/>.</exception>
    public void Report(int completed, int total, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Total = Math.Max(0, total);
        Completed = Math.Clamp(completed, 0, Total);
        IsIndeterminate = false;
        Text = text;
        Changed();
    }

    /// <summary>
    ///     Reports a step whose amount of work is unknown: the bar becomes indeterminate.
    /// </summary>
    /// <param name="text">The status text to show, for example <c>Saving...</c>. Must not be
    ///     <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is
    ///     <see langword="null"/>.</exception>
    public void SetIndeterminate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        IsIndeterminate = true;
        Text = text;
        Changed();
    }

    /// <summary>
    ///     Runs the cancel action once, when the operation can be canceled now; the Cancel button then
    ///     disappears, and the strip may show another running operation instead.
    /// </summary>
    internal void RequestCancel()
    {
        if (!CanCancel)
        {
            return;
        }

        _cancelRequested = true;
        Changed();
        _cancel!();
    }

    /// <summary>
    ///     Ends the operation: the strip shows the running operation that should be shown next, or goes
    ///     idle.
    /// </summary>
    public void Dispose()
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        _owner.End(this);
    }

    /// <summary>
    ///     Tells the strip this operation changed, unless it has ended.
    /// </summary>
    private void Changed()
    {
        if (!_ended)
        {
            _owner.OnOperationChanged(this);
        }
    }
}
