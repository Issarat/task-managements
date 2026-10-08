namespace TaskManager.WebUI.Models;

public sealed record ApiResponse<T>(
    bool Success,
    string Message,
    T? Data = default,
    object? Errors = null);
