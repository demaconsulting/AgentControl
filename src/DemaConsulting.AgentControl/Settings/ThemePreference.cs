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

namespace DemaConsulting.AgentControl.Settings;

/// <summary>
///     Which color theme the user wants AgentControl to use.
/// </summary>
/// <remarks>
///     <see cref="System"/> is the default, and also what a settings file without the value (one
///     written by an older version) means, so existing users keep seeing the theme their
///     operating system uses. The persisted form is the member's name (see <c>AppSettings</c>).
/// </remarks>
internal enum ThemePreference
{
    /// <summary>
    ///     Follow the operating system's light or dark setting, including while the application
    ///     runs.
    /// </summary>
    System,

    /// <summary>
    ///     Always use the light theme.
    /// </summary>
    Light,

    /// <summary>
    ///     Always use the dark theme.
    /// </summary>
    Dark
}
