namespace TaskManager.WebUI.Services;

public sealed class BoardOwnerCookieService(
    IHttpContextAccessor httpContextAccessor)
{
    private const string CookiePrefix = "taskflow_board_owner_";

    public string? GetToken(string publicId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicId);

        return GetHttpContext().Request.Cookies.TryGetValue(
            GetCookieName(publicId),
            out var token)
            ? token
            : null;
    }

    public void SetToken(
        string publicId,
        string ownerToken,
        DateTime expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerToken);

        GetHttpContext().Response.Cookies.Append(
            GetCookieName(publicId),
            ownerToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Path = "/",
                Expires = new DateTimeOffset(
                    DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc))
            });
    }

    private static string GetCookieName(string publicId) =>
        $"{CookiePrefix}{publicId}";

    private HttpContext GetHttpContext()
    {
        return httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "HTTP context is not available.");
    }
}
