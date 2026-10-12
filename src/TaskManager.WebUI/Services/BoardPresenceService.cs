using TaskManager.WebUI.Models;

namespace TaskManager.WebUI.Services;

// A singleton for a single app instance. SignalR Hub instances are transient.
public sealed class BoardPresenceService(TimeProvider timeProvider)
{
    private readonly object gate = new();
    private readonly Dictionary<string, Connection> connections = new(StringComparer.Ordinal);
    private long revision;

    public BoardPresenceSnapshot Join(string connectionId, string publicId,
        OnlineBoardMemberModel member, DateTime expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (expiresAtUtc <= timeProvider.GetUtcNow().UtcDateTime)
                throw new InvalidOperationException("The board session has expired.");
            if (connections.TryGetValue(connectionId, out var existing)
                && existing.PublicId != publicId)
                throw new InvalidOperationException("A connection can only join one board.");

            connections[connectionId] = new(publicId, member, expiresAtUtc, existing?.EditingTaskId);
            return Snapshot(publicId);
        }
    }

    public BoardPresenceSnapshot SetEditing(string connectionId, string publicId, Guid? taskItemId,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!connections.TryGetValue(connectionId, out var connection)
                || connection.PublicId != publicId
                || connection.ExpiresAtUtc <= timeProvider.GetUtcNow().UtcDateTime)
                throw new InvalidOperationException("The connection has no active board session.");
            connections[connectionId] = connection with { EditingTaskId = taskItemId };
            return Snapshot(publicId);
        }
    }

    public BoardPresenceSnapshot? Leave(string connectionId)
    {
        lock (gate)
        {
            return connections.Remove(connectionId, out var connection)
                ? Snapshot(connection.PublicId) : null;
        }
    }

    public ExpiredBoardPresence RemoveExpired()
    {
        lock (gate)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var expired = connections.Where(pair => pair.Value.ExpiresAtUtc <= now)
                .Select(pair => new ExpiredBoardConnection(pair.Key, pair.Value.PublicId))
                .ToArray();
            foreach (var connection in expired)
                connections.Remove(connection.ConnectionId);
            var snapshots = expired.Select(connection => connection.PublicId)
                .Distinct(StringComparer.Ordinal).Select(Snapshot).ToArray();
            return new(expired, snapshots);
        }
    }

    private BoardPresenceSnapshot Snapshot(string publicId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var active = connections.Values
            .Where(connection => connection.PublicId == publicId && connection.ExpiresAtUtc > now)
            .ToArray();
        var members = active
            .GroupBy(connection => connection.Member.GuestId)
            .Select(group => group.First().Member)
            .OrderBy(member => member.DisplayName, StringComparer.Ordinal)
            .ThenBy(member => member.GuestId)
            .ToArray();
        // A global monotonic revision avoids retaining entries for empty boards.
        // Browsers ignore delayed snapshots with older revisions.
        return new(publicId, ++revision, members)
        {
            EditingTasks = active.Where(connection => connection.EditingTaskId.HasValue)
                .GroupBy(connection => connection.EditingTaskId!.Value)
                .OrderBy(group => group.Key)
                .Select(group => new TaskEditingPresenceModel(group.Key,
                    group.GroupBy(connection => connection.Member.GuestId)
                        .Select(editors => editors.First().Member)
                        .OrderBy(member => member.DisplayName, StringComparer.Ordinal)
                        .ThenBy(member => member.GuestId).ToArray()))
                .ToArray()
        };
    }

    private sealed record Connection(string PublicId, OnlineBoardMemberModel Member,
        DateTime ExpiresAtUtc, Guid? EditingTaskId);
}
