using Microsoft.AspNetCore.SignalR;
using TaskManager.Application.Interfaces;

namespace TaskManager.WebUI.Hubs;

public sealed class BoardHub(
    IGuestService guestService,
    IBoardService boardService) : Hub
{
    public async Task JoinBoard(string publicId)
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

        // Service คืนเฉพาะบอร์ดที่ Guest เป็นสมาชิกและยังไม่หมดอายุ
        var boards = await boardService.GetListBySessionIdAsync(
            guest.Id,
            cancellationToken);

        if (!boards.Any(board => board.PublicId == publicId))
            throw new HubException("ไม่สามารถเข้าร่วมบอร์ดนี้ได้");

        // เพิ่มการเชื่อมต่อของแท็บนี้เข้ากลุ่ม
        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            $"board:{publicId}",
            cancellationToken);
    }

}
