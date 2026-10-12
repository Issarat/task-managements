using Microsoft.AspNetCore.SignalR;
using TaskManager.Application.Interfaces;
using TaskManager.WebUI.Models;
using TaskManager.WebUI.Services;

namespace TaskManager.WebUI.Hubs;

public sealed class BoardHub(
    IGuestService guestService,
    IBoardService boardService,
    BoardPresenceService presence,
    ILogger<BoardHub> logger) : Hub
{
    public async Task<BoardPresenceSnapshot> JoinBoard(string publicId)
    {
        var cancellationToken = Context.ConnectionAborted;

        if (string.IsNullOrWhiteSpace(publicId) || publicId.Length > 32)
            throw new HubException("ข้อมูลบอร์ดไม่ถูกต้อง");

        // อ่าน Cookie จากการเชื่อมต่อ ไม่รับ token จาก JavaScript
        var httpContext = Context.GetHttpContext();
        var guestToken = httpContext?.Request.Cookies["taskflow_guest"];

        if (string.IsNullOrWhiteSpace(guestToken))
            throw new HubException("กรุณาเริ่มทดลองใช้งานก่อน");

        // Service ตรวจ hash และวันหมดอายุของ Guest
        var guest = await guestService.GetGuestByTokenAsync(
            guestToken,
            cancellationToken);

        if (guest is null)
            throw new HubException("Session หมดอายุ กรุณาเริ่มใหม่");

        var board = await boardService.GetPreviewAsync(publicId, cancellationToken);
        if (board is null)
            throw new HubException("ไม่สามารถเข้าร่วมบอร์ดนี้ได้");

        // Service คืนเฉพาะบอร์ดที่ Guest เป็นสมาชิกและยังไม่หมดอายุ
        var boards = await boardService.GetListBySessionIdAsync(
            guest.Id,
            cancellationToken);

        if (!boards.Any(board => board.PublicId == publicId))
            throw new HubException("ไม่สามารถเข้าร่วมบอร์ดนี้ได้");

        if (Context.Items.TryGetValue("BoardPublicId", out var joinedBoard)
            && !string.Equals(joinedBoard as string, publicId, StringComparison.Ordinal))
            throw new HubException("การเชื่อมต่อนี้เข้าร่วมบอร์ดอื่นแล้ว");

        // เพิ่มการเชื่อมต่อของแท็บนี้เข้ากลุ่ม
        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            $"board:{publicId}",
            cancellationToken);

        Context.Items["BoardPublicId"] = publicId;
        var expiresAtUtc = guest.ExpiresAtUtc < board.ExpiresAtUtc
            ? guest.ExpiresAtUtc : board.ExpiresAtUtc;
        BoardPresenceSnapshot snapshot;
        try
        {
            snapshot = presence.Join(Context.ConnectionId, publicId,
                new(guest.Id, guest.DisplayName, guest.ShortName, guest.AvatarColor),
                expiresAtUtc, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"board:{publicId}");
            throw new HubException("ไม่สามารถเข้าร่วมบอร์ดนี้ได้");
        }
        await BroadcastPresenceAsync(snapshot);
        return snapshot;
    }

    public async Task<BoardPresenceSnapshot> StartEditingTask(string publicId, Guid taskItemId)
    {
        RequireJoinedBoard(publicId);
        var cancellationToken = Context.ConnectionAborted;
        var token = Context.GetHttpContext()?.Request.Cookies["taskflow_guest"];
        var guest = string.IsNullOrWhiteSpace(token) ? null
            : await guestService.GetGuestByTokenAsync(token, cancellationToken);
        if (guest is null)
            throw new HubException("Session หมดอายุ กรุณาเริ่มใหม่");
        var boards = await boardService.GetListBySessionIdAsync(guest.Id, cancellationToken);
        // Check membership before GetAsync, which can enroll a guest on a public board.
        if (!boards.Any(board => board.PublicId == publicId))
            throw new HubException("ไม่สามารถเข้าร่วมบอร์ดนี้ได้");
        var board = await boardService.GetAsync(publicId, guest.Id, cancellationToken);
        if (board is null || !board.Tasks.Any(task => task.TaskItemId == taskItemId))
            throw new HubException("ไม่พบงานในบอร์ดนี้");
        return await ChangeEditingAsync(publicId, taskItemId);
    }

    public Task<BoardPresenceSnapshot> StopEditingTask(string publicId)
    {
        RequireJoinedBoard(publicId);
        return ChangeEditingAsync(publicId, null);
    }

    private void RequireJoinedBoard(string publicId)
    {
        if (!Context.Items.TryGetValue("BoardPublicId", out var joinedBoard)
            || !string.Equals(joinedBoard as string, publicId, StringComparison.Ordinal))
            throw new HubException("กรุณาเข้าร่วมบอร์ดนี้ก่อน");
    }

    private async Task<BoardPresenceSnapshot> ChangeEditingAsync(string publicId, Guid? taskItemId)
    {
        BoardPresenceSnapshot snapshot;
        try
        {
            snapshot = presence.SetEditing(Context.ConnectionId, publicId, taskItemId,
                Context.ConnectionAborted);
        }
        catch (InvalidOperationException)
        {
            throw new HubException("Session หมดอายุ กรุณาเริ่มใหม่");
        }
        await BroadcastPresenceAsync(snapshot);
        return snapshot;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var snapshot = presence.Leave(Context.ConnectionId);
        if (snapshot is not null)
            await BroadcastPresenceAsync(snapshot);
        await base.OnDisconnectedAsync(exception);
    }

    private async Task BroadcastPresenceAsync(BoardPresenceSnapshot snapshot)
    {
        try
        {
            await Clients.Group($"board:{snapshot.PublicId}")
                .SendAsync("BoardPresenceChanged", snapshot);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not publish board presence.");
        }
    }
}
