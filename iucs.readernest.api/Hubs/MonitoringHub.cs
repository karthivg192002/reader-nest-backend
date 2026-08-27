using iucs.readernest.api.Auth;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.SignalR;

namespace iucs.readernest.api.Hubs
{
    /// <summary>
    /// Push-only: MonitoringBroadcastService periodically sends every connected client the
    /// same "MonitoringUpdate" payload (the full MonitoringSummaryDto, unchanged from what
    /// GET /api/monitoring/summary already returns) — there's nothing for a client to invoke.
    /// Gated by the same SystemMonitoring:View permission as the REST endpoint, so anyone who
    /// can open a connection here is already allowed to see this data.
    /// </summary>
    [HasPermission(PermissionModule.SystemMonitoring, PermissionAction.View)]
    public class MonitoringHub : Hub
    {
        private readonly MonitoringConnectionTracker _tracker;

        public MonitoringHub(MonitoringConnectionTracker tracker)
        {
            _tracker = tracker;
        }

        public override Task OnConnectedAsync()
        {
            _tracker.Increment();
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            _tracker.Decrement();
            return base.OnDisconnectedAsync(exception);
        }
    }

    /// <summary>Lets MonitoringBroadcastService skip its Prometheus round-trip entirely when nobody has the monitoring page open.</summary>
    public class MonitoringConnectionTracker
    {
        private int _count;

        public void Increment() => Interlocked.Increment(ref _count);
        public void Decrement() => Interlocked.Decrement(ref _count);
        public bool HasConnections => Volatile.Read(ref _count) > 0;
    }
}
