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
using DemaConsulting.AgentControl.LauncherUI;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Test-only helpers that let a test run one of <see cref="RepoCardViewModel"/>'s asynchronous
///     operations to completion in a single statement.
/// </summary>
/// <remarks>
///     The application itself never blocks on these operations; the helpers exist only so that the
///     many tests which simply want "do this, then check the outcome" stay readable. Each helper
///     runs the operation on a pool thread (no synchronization context), so waiting for it cannot
///     deadlock the way waiting on a UI thread would. Tests of the asynchronous behavior itself
///     (progress, cancellation, double execution) await the real asynchronous members directly.
/// </remarks>
internal static class RepoCardViewModelTestExtensions
{
    /// <summary>
    ///     Runs every refresh of a card to completion: the cheap checks, the upgrade check and the
    ///     working-tree status check.
    /// </summary>
    /// <param name="card">The card to refresh.</param>
    public static void Refresh(this RepoCardViewModel card)
    {
        card.RefreshCheap();
        RunToCompletion(() => card.RefreshUpgradeStatusAsync());
        card.RefreshDirtyStatus();
    }

    /// <summary>
    ///     Applies a package selection to a card and waits for the apply to finish.
    /// </summary>
    /// <param name="card">The card to apply the package to.</param>
    /// <param name="packageName">The selected package ID.</param>
    /// <param name="version">The selected version.</param>
    /// <param name="sourceAlias">The alias of the source the selection was made from; when
    ///     <see langword="null"/>, the <c>Default</c> source if configured, otherwise the first
    ///     configured source.</param>
    public static void ApplySelectedPackage(
        this RepoCardViewModel card, string packageName, string version, string? sourceAlias = null)
    {
        var alias = sourceAlias ?? ChooseDefaultAlias(card);
        RunToCompletion(() => card.ApplySelectedPackageAsync(new PackageReference(alias, packageName, version)));
    }

    /// <summary>
    ///     Runs the best-effort pre-launch sync of a card and waits for its outcome.
    /// </summary>
    /// <param name="card">The card to sync.</param>
    /// <returns>The sync's result, as returned by the card.</returns>
    public static bool EnsureAgentFilesSyncedBeforeLaunch(this RepoCardViewModel card) =>
        RunToCompletion(() => card.EnsureAgentFilesSyncedBeforeLaunchAsync());

    /// <summary>
    ///     Runs an asynchronous operation on a pool thread and waits for it.
    /// </summary>
    /// <param name="operation">Starts the operation.</param>
    private static void RunToCompletion(Func<Task> operation) =>
        Task.Run(operation).GetAwaiter().GetResult();

    /// <summary>
    ///     Runs an asynchronous operation on a pool thread and waits for its result.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="operation">Starts the operation.</param>
    /// <returns>The operation's result.</returns>
    private static T RunToCompletion<T>(Func<Task<T>> operation) =>
        Task.Run(operation).GetAwaiter().GetResult();

    /// <summary>
    ///     Chooses the source alias a selection is made from when a test does not name one.
    /// </summary>
    /// <param name="card">The card whose configured sources are consulted.</param>
    /// <returns>The <c>Default</c> alias when configured, otherwise the first configured alias.</returns>
    private static string ChooseDefaultAlias(RepoCardViewModel card)
    {
        var registry = card.CreateSourceRegistry();
        if (registry.Definitions.Count == 0 || registry.Contains(PackageSourceDefinition.DefaultAlias))
        {
            return PackageSourceDefinition.DefaultAlias;
        }

        return registry.Definitions[0].Alias;
    }
}
