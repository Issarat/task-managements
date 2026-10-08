namespace TaskManager.Application.Models;


/// <summary>
/// ข้อมูล Guest ที่อนุญาตให้ส่งกลับไปแสดงบน UI
/// </summary>
public sealed record GuestResponseModel(
    Guid Id,
    string DisplayName,
    string ShortName,
    string AvatarColor,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc);

/// <summary>
/// ผลลัพธ์การสร้าง Guest ใหม่ โดย GuestToken ใช้เขียนลง HttpOnly Cookie เท่านั้น
/// </summary>
public sealed record CreatedGuestModel(
    string GuestToken,
    GuestResponseModel Guest);

/// <summary>
/// ข้อมูลสำหรับสร้าง AnonymousUser ในระบบ
/// </summary>
public sealed record CreateGuestModel(
    string SessionTokenHash,
    string DisplayName,
    string AvatarColor,
    DateTime ExpiresAtUtc);
