namespace TaskManager.WebUI.Models;

// Only public profile information is sent to browsers; never cookie tokens/hashes.
public sealed record OnlineBoardMemberModel(
    Guid GuestId, string DisplayName, string ShortName, string AvatarColor);

public sealed record BoardPresenceSnapshot(
    string PublicId, long Revision, IReadOnlyList<OnlineBoardMemberModel> Members);

public sealed record ExpiredBoardConnection(string ConnectionId, string PublicId);

public sealed record ExpiredBoardPresence(
    IReadOnlyList<ExpiredBoardConnection> Connections,
    IReadOnlyList<BoardPresenceSnapshot> Snapshots);
