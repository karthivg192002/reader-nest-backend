using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Bulk "which of these sessions still has a watchable recording" lookup, shared by every
    /// caller that maps a batch of ClassSession rows to ClassSessionDto (SessionService,
    /// ParentPortalService) — ToDto() itself can't do this (it's a plain synchronous mapper over
    /// an already-loaded entity), and HasRecording/RecordingExpiresAtUtc default to false/null
    /// when nobody supplies them, so skipping this call silently shows every session as having
    /// no recording regardless of what was actually registered.
    /// </summary>
    public static class SessionRecordingLookup
    {
        /// <summary>Session id → that recording's own expiry (the latest one, if more than one was registered) — only for sessions with at least one non-expired recording.</summary>
        public static async Task<Dictionary<Guid, DateTime?>> ActiveRecordingsBySessionAsync(
            IUnitOfWork unitOfWork, IEnumerable<Guid> sessionIds, CancellationToken cancellationToken = default)
        {
            var ids = sessionIds.ToList();
            if (ids.Count == 0) return [];
            var now = DateTime.UtcNow;
            var rows = await unitOfWork.Repository<SessionRecording>().Query()
                .Where(r => ids.Contains(r.ClassSessionId) && (r.ExpiresAtUtc == null || r.ExpiresAtUtc > now))
                .GroupBy(r => r.ClassSessionId)
                .Select(g => new { SessionId = g.Key, ExpiresAtUtc = g.Max(r => r.ExpiresAtUtc) })
                .ToListAsync(cancellationToken);
            return rows.ToDictionary(r => r.SessionId, r => r.ExpiresAtUtc);
        }
    }
}
