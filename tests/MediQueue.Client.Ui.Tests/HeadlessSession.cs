using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

namespace MediQueue.Client.Ui.Tests;

/// <summary>
/// A minimal Avalonia application for the headless session to host.
/// </summary>
/// <remarks>
/// Not the doctor shell's own <c>App</c>: that one builds a container, reads
/// configuration and opens a window against a server. What these tests need is
/// a real Avalonia dispatcher and a real binding system, which is the smallest
/// application that can exist.
/// </remarks>
public sealed class TestApp : Application
{
    /// <inheritdoc />
    public override void Initialize() => Styles.Add(new FluentTheme());

    /// <summary>The entry point the headless session looks for by convention.</summary>
    /// <returns>The configured builder.</returns>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>
/// Starts one Avalonia UI thread and shares it across the whole class.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="HeadlessUnitTestSession"/> is the same mechanism the
/// <c>Avalonia.Headless.XUnit</c> attributes wrap. That package is not used
/// here because it depends on <c>xunit.v3</c>, and this solution runs xUnit v2
/// everywhere else — a second test framework in one solution costs more than an
/// attribute saves.
/// </para>
/// <para>
/// The session owns a real dispatcher on a real thread, so
/// <c>Dispatcher.UIThread</c> means something inside <see cref="OnUiAsync"/>
/// and means something different outside it. That distinction is the entire
/// subject of these tests.
/// </para>
/// </remarks>
public sealed class HeadlessSession : IDisposable
{
    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(TestApp));

    /// <summary>Runs work on Avalonia's UI thread and waits for it to finish.</summary>
    /// <remarks>
    /// <para>
    /// <strong>The generic argument is load-bearing and the obvious call is
    /// wrong.</strong> Passing a <c>Func&lt;Task&gt;</c> straight to
    /// <c>Dispatch</c> binds the <c>Func&lt;T&gt;</c> overload with
    /// <c>T = Task</c>: the session then treats the returned task as a *value*,
    /// runs the lambda as far as its first <c>await</c>, and hands the
    /// unfinished task back. Awaiting the result waits for the dispatch, not for
    /// the work.
    /// </para>
    /// <para>
    /// Every assertion inside such a lambda is then unreachable, and the test
    /// passes. Two of these tests did exactly that before it was caught — the
    /// same shape as D-65, in a new harness. Returning a value forces the
    /// <c>Func&lt;Task&lt;T&gt;&gt;</c> overload, which awaits properly.
    /// </para>
    /// </remarks>
    /// <param name="work">What to run.</param>
    public Task OnUiAsync(Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        return _session.Dispatch(
            async () =>
            {
                await work().ConfigureAwait(true);

                return true;
            },
            CancellationToken.None);
    }

    /// <inheritdoc />
    public void Dispose() => _session.Dispose();
}

/// <summary>Binds the UI tests to one Avalonia thread rather than one per class.</summary>
[CollectionDefinition(Name)]
public sealed class HeadlessCollection : ICollectionFixture<HeadlessSession>
{
    /// <summary>The collection name the test classes reference.</summary>
    public const string Name = "avalonia-headless";
}
