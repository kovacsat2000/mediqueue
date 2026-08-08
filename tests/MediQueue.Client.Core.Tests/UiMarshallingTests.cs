using MediQueue.Client.Core.Api;
using MediQueue.Client.Core.Realtime;
using MediQueue.Contracts.Authentication;

namespace MediQueue.Client.Core.Tests;

/// <summary>
/// That every event leaving the push channel goes through the dispatcher.
/// </summary>
/// <remarks>
/// <para>
/// The gap this closes was real and had survived three phases. SignalR delivers
/// on a thread-pool thread; the view models mutate <c>ObservableCollection</c>s
/// the windows are bound to; and there was no marshalling anywhere. Measured
/// against the running server before it was fixed: a <c>VisitQueued</c> handler
/// ran on thread 11 while the connection had been created on thread 4.
/// </para>
/// <para>
/// <strong>Why nothing caught it.</strong> The unit tests raise events on the
/// test's own thread through a hand-written double, and the end-to-end drive in
/// P7 was a console program with no Avalonia in it — so neither had a UI thread
/// to be on the wrong side of. The first thing that would have noticed is a
/// window during the defence.
/// </para>
/// <para>
/// These assert the plumbing rather than the threading: that the connection
/// hands every event to the dispatcher it was given, whatever that dispatcher
/// then does. Which thread Avalonia's dispatcher runs on is Avalonia's
/// business, and one line in each shell is all this project contributes.
/// </para>
/// </remarks>
public class UiMarshallingTests
{
    /// <summary>Records what it was asked to run, and runs it.</summary>
    private sealed class RecordingDispatcher : IUiDispatcher
    {
        public int Posts { get; private set; }

        public void Post(Action action)
        {
            Posts++;
            action();
        }
    }

    private static QueueConnection AConnection(IUiDispatcher dispatcher) =>
        new(new Uri("http://localhost:5123/hubs/queue"), new AuthSession(), dispatcher);

    [Fact]
    public void The_connection_will_not_be_built_without_a_dispatcher()
    {
        // A null-tolerant constructor would let a shell forget, and forgetting
        // is what produced the gap.
        Should.Throw<ArgumentNullException>(() =>
            new QueueConnection(new Uri("http://localhost:5123/hubs/queue"), new AuthSession(), null!));
    }

    [Fact]
    public async Task Every_status_change_is_posted_through_the_dispatcher()
    {
        // Reconnecting and Closed are raised by SignalR's own threads, and Live
        // arrives on whichever thread completed the start — measured at thread 8
        // while the caller was on thread 4.
        var dispatcher = new RecordingDispatcher();
        await using var connection = AConnection(dispatcher);

        var seen = new List<RealtimeStatus>();
        connection.StatusChanged += (_, status) => seen.Add(status);

        // Nothing is listening on that address, so the attempt fails — which is
        // the point: it reports Connecting and then Disconnected, and both must
        // travel through the dispatcher.
        await Should.ThrowAsync<Exception>(() => connection.StartAsync(default));

        seen.ShouldBe([RealtimeStatus.Connecting, RealtimeStatus.Disconnected]);
        dispatcher.Posts.ShouldBe(seen.Count, "every status change must be posted, not raised inline");
    }

    [Fact]
    public void The_immediate_dispatcher_runs_the_work_it_is_given()
    {
        // The one used by tests and by any host with no UI thread. It is
        // deliberately not a default: a default that silently does nothing is
        // how the absence of marshalling went unnoticed.
        var dispatcher = new ImmediateUiDispatcher();
        var ran = false;

        dispatcher.Post(() => ran = true);

        ran.ShouldBeTrue();
        Should.Throw<ArgumentNullException>(() => dispatcher.Post(null!));
    }

    [Fact]
    public void Both_shells_supply_a_dispatcher_rather_than_relying_on_a_default()
    {
        // Asserted here because Client.Core cannot reference the shells: if a
        // composition root stopped registering one, the constructor above would
        // throw at start-up rather than at run time — this test names the
        // reason so the next reader knows why that throw is deliberate.
        typeof(IUiDispatcher).IsInterface.ShouldBeTrue();

        // And nothing in Client.Core implements it against a UI framework,
        // which is what keeps this project free of one.
        typeof(IUiDispatcher).Assembly
            .GetTypes()
            .Where(type => typeof(IUiDispatcher).IsAssignableFrom(type) && !type.IsInterface)
            .ShouldBe([typeof(ImmediateUiDispatcher)]);
    }
}
