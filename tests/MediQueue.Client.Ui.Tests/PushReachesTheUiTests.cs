using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using MediQueue.Client.Core.Realtime;
using MediQueue.Client.Core.ViewModels;
using MediQueue.Client.Doctor;
using MediQueue.Contracts.Visits;

namespace MediQueue.Client.Ui.Tests;

/// <summary>
/// The last link in the push chain, with a real Avalonia binding on the other
/// end of it.
/// </summary>
/// <remarks>
/// <para>
/// Everything before this link is already covered without a window: four tests
/// prove <c>QueueConnection</c> hands every event to the dispatcher it was
/// given. What those cannot say is whether <em>the dispatcher the shells
/// nominate</em> is the one Avalonia requires — that is a fact about Avalonia,
/// and only Avalonia can answer it.
/// </para>
/// <para>
/// The gap being closed was real: until it was fixed, nothing in this system
/// marshalled anything, and a push handler was measured running on thread 11
/// while the connection had been created on thread 4 (D-74).
/// </para>
/// <para>
/// <strong>What these tests do not prove, measured rather than assumed.</strong>
/// Avalonia 12.1.1's headless platform does <em>not</em> raise when a bound
/// <c>ObservableCollection</c> is mutated from a background thread — tried with
/// a realised <c>ListBox</c> carrying a selection, which is what the doctor's
/// queue is. So the earlier claim that the clients were "one push away from a
/// crash" is unproven, and is not made here. What is proven is that the push
/// now arrives on Avalonia's own thread, which is what the framework's contract
/// asks for and what a real windowing backend may well enforce more strictly
/// than the headless one does.
/// </para>
/// </remarks>
[Collection(HeadlessCollection.Name)]
public class PushReachesTheUiTests(HeadlessSession session)
{
    private static readonly DateTimeOffset EightUtc = new(2026, 8, 6, 8, 0, 0, TimeSpan.Zero);

    /// <summary>An items control genuinely bound to a collection, inside a window.</summary>
    /// <remarks>
    /// A shown window, not a bare control: an unattached control has no binding
    /// subscription and no visual tree, so mutating its source from the wrong
    /// thread would upset nothing. The point is to have something Avalonia is
    /// really watching.
    /// </remarks>
    private static (Window Window, ItemsControl List) ABoundList(ObservableCollection<QueueRow> rows)
    {
        var list = new ItemsControl();
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding { Source = rows, Path = string.Empty });

        var window = new Window { Content = list, Width = 400, Height = 300 };
        window.Show();

        // Drains the layout and binding queues. ILayoutRoot.LayoutManager is
        // internal in Avalonia 12, and this is the supported way to reach the
        // same point from a test.
        Dispatcher.UIThread.RunJobs();

        return (window, list);
    }

    private static QueueRow ARow(string name, DateTimeOffset queuedAt) =>
        new(Guid.CreateVersion7(queuedAt), queuedAt, name, "123-456-788", "Fejfájás", "10:00", VisitStatus.Waiting);

    /// <summary>Runs work on a thread pool thread and waits for it, as SignalR would.</summary>
    private static async Task FromABackgroundThreadAsync(Action work)
    {
        var done = new TaskCompletionSource();

        _ = Task.Run(() =>
        {
            try
            {
                Dispatcher.UIThread.CheckAccess().ShouldBeFalse(
                    "this work must not already be on the UI thread, or the test proves nothing");

                work();
                done.SetResult();
            }
            catch (Exception exception)
            {
                done.SetException(exception);
            }
        });

        await done.Task;
    }

    [Fact]
    public async Task A_push_from_a_background_thread_reaches_a_bound_control()
    {
        // The assertion this project exists for.
        Exception? unhandled = null;

        await session.OnUiAsync(async () =>
        {
            var rows = new ObservableCollection<QueueRow>();
            var (window, list) = ABoundList(rows);

            list.ItemCount.ShouldBe(0);

            // The shell's own dispatcher, resolved on the UI thread as the
            // composition root does, then used from somewhere else entirely.
            var dispatcher = new AvaloniaUiDispatcher();
            var applied = new TaskCompletionSource();

            await FromABackgroundThreadAsync(() => dispatcher.Post(() =>
            {
                try
                {
                    rows.Add(ARow("Kovács Anna", EightUtc));
                    applied.SetResult();
                }
                catch (Exception exception)
                {
                    unhandled = exception;
                    applied.SetResult();
                }
            }));

            // Let the posted work run and the binding settle.
            await WaitForUiAsync(() => applied.Task.IsCompleted);
            Dispatcher.UIThread.RunJobs();

            unhandled.ShouldBeNull("mutating a bound collection through the dispatcher must not throw");
            rows.Count.ShouldBe(1);
            list.ItemCount.ShouldBe(1, "the control Avalonia is binding to must have seen the new row");

            window.Close();
        });
    }

    [Fact]
    public async Task Without_the_dispatcher_the_same_work_runs_off_the_user_interface_thread()
    {
        // The control case, rewritten after measuring what actually happens.
        //
        // It was first written to assert that Avalonia *refuses* a bound
        // collection mutated from a background thread. It does not — measured
        // with a realised ListBox carrying a selection, which is exactly what
        // the doctor's queue is: no exception on the background thread, none on
        // the UI thread afterwards, and the control saw the row.
        //
        // So the honest control is the one below: the two paths genuinely
        // differ, and only one of them ends on Avalonia's thread. Whether a
        // real windowing backend is as forgiving as the headless one is not
        // something this test can answer, and the fix does not depend on the
        // answer — see the class remarks.
        var ranOnUiThread = true;

        await session.OnUiAsync(async () =>
        {
            var rows = new ObservableCollection<QueueRow>();
            var (window, _) = ABoundList(rows);

            await FromABackgroundThreadAsync(() =>
            {
                // Deliberately not through the dispatcher: this is what the
                // clients did in every phase up to the fix.
                ranOnUiThread = Dispatcher.UIThread.CheckAccess();
                rows.Add(ARow("Nagy Piroska", EightUtc));
            });

            window.Close();
        });

        ranOnUiThread.ShouldBeFalse(
            "without the dispatcher the mutation happens off the UI thread, which is the "
            + "difference the marshalling exists to remove");
    }

    [Fact]
    public async Task The_dispatcher_the_shells_nominate_is_avalonias_own()
    {
        // Stated as its own assertion because it is the sentence the previous
        // phase could not write: what was proven then was that events reach
        // whatever dispatcher is supplied, not that the supplied one is right.
        await session.OnUiAsync(async () =>
        {
            var dispatcher = new AvaloniaUiDispatcher();
            var ranOnUiThread = false;
            var done = new TaskCompletionSource();

            await FromABackgroundThreadAsync(() => dispatcher.Post(() =>
            {
                ranOnUiThread = Dispatcher.UIThread.CheckAccess();
                done.SetResult();
            }));

            await WaitForUiAsync(() => done.Task.IsCompleted);

            ranOnUiThread.ShouldBeTrue(
                "work posted from a background thread must arrive on Avalonia's UI thread");
        });
    }

    /// <summary>
    /// Lets queued UI work run while staying on the UI thread.
    /// </summary>
    /// <remarks>
    /// <c>Dispatcher.UIThread.RunJobs()</c> rather than a delay: the work is
    /// already queued, so this drains it deterministically instead of hoping a
    /// sleep was long enough.
    /// </remarks>
    private static async Task WaitForUiAsync(Func<bool> until)
    {
        for (var attempt = 0; attempt < 200 && !until(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }

        until().ShouldBeTrue("the posted work never ran");
    }
}
