using System.Security.Cryptography;
using System.Text;
using TaskManager.Application.Interfaces;
using TaskManager.Application.Models;
using TaskManager.Domain.Entities;
using TaskManager.Domain.Extensions;

namespace TaskManager.Application.Services;

public sealed class GuestService(
    IAnonymousUserRepository anonymousUserRepository) : IGuestService
{
    private static readonly TimeSpan CookieLifetime = TimeSpan.FromHours(24);

    public async Task<CreatedGuestModel> GenerateGuestAsync(
          CancellationToken cancellationToken = default)
    {
        var (guestToken, dbHash) = CreateSessionToken();
        var avatar = AvatarExtension.GenerateRandom();
        var now = DateTime.UtcNow;

        var anonymousUser = new AnonymousUser
        {
            SessionTokenHash = dbHash,
            DisplayName = avatar.DisplayName,
            AvatarColor = avatar.Color,
            CreatedAtUtc = now,
            LastSeenAtUtc = now,
            ExpiresAtUtc = now.Add(CookieLifetime)
        };

        await anonymousUserRepository.AddAsync(
            anonymousUser,
            cancellationToken);

        return new CreatedGuestModel(
            guestToken,
            MapGuest(anonymousUser));
    }

    public async Task<GuestResponseModel?> GetGuestByTokenAsync(
        string guestToken,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = ComputeHash(guestToken);

        var anonymousUser =
            await anonymousUserRepository.GetBySessionTokenHashAsync(
                tokenHash,
                cancellationToken);

        if (anonymousUser is null || anonymousUser.IsExpired(DateTime.UtcNow))
        {
            return null;
        }

        await anonymousUserRepository.UpdateLastSeenAsync(
            anonymousUser.Id,
            DateTime.UtcNow,
            cancellationToken);

        return MapGuest(anonymousUser);
    }

    private static GuestResponseModel MapGuest(AnonymousUser anonymousUser)
    {
        return new GuestResponseModel(
            anonymousUser.Id,
            anonymousUser.DisplayName,
            AvatarExtension.CreateShortName(anonymousUser.DisplayName),
            anonymousUser.AvatarColor,
            anonymousUser.CreatedAtUtc,
            anonymousUser.ExpiresAtUtc);
    }

    /// <summary>
    /// สร้าง Guest session token แบบสุ่มที่ปลอดภัย
    /// พร้อม SHA-256 hash สำหรับจัดเก็บในฐานข้อมูล
    /// </summary>
    /// <returns>
    /// <para>
    /// GuestToken: token จริงสำหรับเขียนลง HttpOnly Cookie เท่านั้น
    /// </para>
    /// <para>
    /// DbHash: SHA-256 hash ขนาด 64 ตัวอักษรสำหรับเก็บในฐานข้อมูล
    /// </para>
    /// </returns>
    private static (string GuestToken, string DbHash)
    CreateSessionToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(32);

        var guestToken = Convert
            .ToBase64String(randomBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        var dbHash = ComputeHash(guestToken);

        return (guestToken, dbHash);
    }

    /// <summary>
    /// สร้าง SHA-256 hash แบบ Hex 64 ตัวอักษรจาก Guest token
    /// </summary>
    private static string ComputeHash(string guestToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guestToken);

        var inputBytes = Encoding.UTF8.GetBytes(guestToken);
        var hashBytes = SHA256.HashData(inputBytes);

        return Convert
            .ToHexString(hashBytes)
            .ToLowerInvariant();
    }
}
