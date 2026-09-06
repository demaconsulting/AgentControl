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

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     Base class for MVVM view models, providing a minimal <see cref="INotifyPropertyChanged"/>
///     implementation shared by every view model in the <c>LauncherUI</c>/<c>RepoSync</c> view
///     layers.
/// </summary>
/// <remarks>
///     Implemented by hand rather than via a source-generator package (e.g.
///     CommunityToolkit.Mvvm) to keep this project's dependency surface minimal - the view
///     models in this codebase are small enough that the boilerplate saved by a generator would
///     not offset the added package/tooling surface. Not thread-safe: like all Avalonia view
///     models, instances are expected to be created and mutated only on the UI thread.
/// </remarks>
internal abstract class ViewModelBase : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    ///     Sets a backing field to a new value and raises <see cref="PropertyChanged"/> when the
    ///     value actually changes.
    /// </summary>
    /// <typeparam name="T">The property's type.</typeparam>
    /// <param name="field">Reference to the backing field to update.</param>
    /// <param name="value">The new value to assign.</param>
    /// <param name="propertyName">The changed property's name; supplied automatically by the
    ///     compiler via <see cref="CallerMemberNameAttribute"/> when omitted.</param>
    /// <returns>
    ///     <see langword="true"/> if the value changed (and <see cref="PropertyChanged"/> was
    ///     raised); <see langword="false"/> if <paramref name="value"/> equaled the existing
    ///     field value, in which case no event is raised.
    /// </returns>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    ///     Raises <see cref="PropertyChanged"/> for the named property.
    /// </summary>
    /// <param name="propertyName">The changed property's name; supplied automatically by the
    ///     compiler via <see cref="CallerMemberNameAttribute"/> when omitted.</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
