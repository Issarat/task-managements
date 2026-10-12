using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using TaskManager.Domain.Extensions;

namespace TaskManager.WebUI.Controllers;

// Pure formatting of the supplied date; no board/session information or database access.
public sealed class DatesController(IStringLocalizer<SharedResource> text) : Controller
{
    [HttpGet("/dates/format")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Format(string? value)
    {
        if (value?.Length != 10 || !DateOnly.TryParseExact(value, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return BadRequest(new { message = text["TaskInvalidDate"].Value });

        return Json(new { value = date.ToIsoDateString(), full = date.ToFullThaiDatetime(),
            shortText = date.ToShortThaiDatetime() });
    }
}
