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
using Material.Icons;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     One configured package source as shown in the "Select Package" window's source column: its
///     alias, kind and location, and the packages it offers once they have been read.
/// </summary>
/// <remarks>
///     Each source loads independently, so this row has its own loading and failure state: a
///     source that cannot be reached shows its own error without hiding the packages of the other
///     sources. Not thread-safe; created and updated on the UI thread.
/// </remarks>
internal sealed class SelectPackageSourceViewModel : ViewModelBase
{
    private bool _isLoading = true;
    private string? _errorMessage;
    private IReadOnlyList<string> _packageNames = [];

    /// <summary>
    ///     Initializes a new row in the "loading" state.
    /// </summary>
    /// <param name="definition">The source the row describes.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="definition"/> is
    ///     <see langword="null"/>.</exception>
    public SelectPackageSourceViewModel(PackageSourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
    }

    /// <summary>
    ///     Gets the source the row describes.
    /// </summary>
    public PackageSourceDefinition Definition { get; }

    /// <summary>
    ///     Gets the source's alias.
    /// </summary>
    public string Alias => Definition.Alias;

    /// <summary>
    ///     Gets the source's location, shown as the row's tooltip.
    /// </summary>
    public string Location => Definition.Location;

    /// <summary>
    ///     Gets the icon showing the kind of source.
    /// </summary>
    public MaterialIconKind TypeIcon => PackageSourceIcons.GetIcon(Definition);

    /// <summary>
    ///     Gets the name of the kind of source.
    /// </summary>
    public string TypeName => PackageSourceIcons.GetTypeName(Definition);

    /// <summary>
    ///     Gets the packages this source offers; empty until loading finishes or when it failed.
    /// </summary>
    public IReadOnlyList<string> PackageNames
    {
        get => _packageNames;
        private set => SetField(ref _packageNames, value);
    }

    /// <summary>
    ///     Gets a value indicating whether the source's packages are still being read.
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetField(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    /// <summary>
    ///     Gets the reason the source could not be read, or <see langword="null"/> when it was
    ///     read (or is still being read).
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the source could not be read.
    /// </summary>
    public bool HasError => _errorMessage is not null;

    /// <summary>
    ///     Gets the short status shown beside the alias: that the source is loading, unavailable,
    ///     or how many packages it offers.
    /// </summary>
    public string StatusText => GetStatusText();

    /// <summary>
    ///     Describes the source's loading state in a few words.
    /// </summary>
    /// <returns>The status text.</returns>
    private string GetStatusText()
    {
        if (_isLoading)
        {
            return "Loading...";
        }

        if (_errorMessage is not null)
        {
            return "Unavailable";
        }

        return _packageNames.Count == 1 ? "1 package" : $"{_packageNames.Count} packages";
    }

    /// <summary>
    ///     Records the packages the source offers and ends the loading state.
    /// </summary>
    /// <param name="packageNames">The package names read from the source.</param>
    public void SetLoaded(IReadOnlyList<string> packageNames)
    {
        ArgumentNullException.ThrowIfNull(packageNames);

        PackageNames = packageNames;
        ErrorMessage = null;
        IsLoading = false;
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>
    ///     Records that the source could not be read and ends the loading state.
    /// </summary>
    /// <param name="message">The reason, in words a user can act on.</param>
    public void SetFailed(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        PackageNames = [];
        ErrorMessage = message;
        IsLoading = false;
    }
}
