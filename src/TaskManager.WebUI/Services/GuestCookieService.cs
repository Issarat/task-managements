using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace TaskManager.WebUI.Services;

public sealed class GuestCookieService(
    IHttpContextAccessor httpContextAccessor)
{
    private const string CookieName = "taskflow_guest";
    private static readonly TimeSpan CookieLifetime = TimeSpan.FromHours(24);

    public string GetOrCreateToken()
    {
        var existingToken = GetToken();

        if (!string.IsNullOrWhiteSpace(existingToken))
        {
            return existingToken;
        }

        return string.Empty;
    }

    private string? GetToken()
    {
        var context = GetHttpContext();

        return context.Request.Cookies.TryGetValue(CookieName, out var token)
            ? token
            : null;
    }

    public void SetToken(string token, DateTimeOffset expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var context = GetHttpContext();

        context.Response.Cookies.Append(
            CookieName,
            token,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Path = "/",
                Expires = expiresAtUtc
            });
    }

    public void DeleteToken()
    {
        var context = GetHttpContext();

        context.Response.Cookies.Delete(
            CookieName,
            new CookieOptions
            {
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/"
            });
    }

    private HttpContext GetHttpContext()
    {
        return httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "HTTP context is not available.");
    }
}
