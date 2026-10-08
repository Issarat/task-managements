using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using TaskManager.Application.Interfaces;
using TaskManager.Application.Models;
using TaskManager.WebUI.Models;
using TaskManager.WebUI.Services;

namespace TaskManager.WebUI.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly GuestCookieService _guestCookieService;
        private readonly IGuestService _guestService;

        public HomeController(
            ILogger<HomeController> logger,
            GuestCookieService guestCookieService,
            IGuestService guestService)
        {
            _logger = logger;
            _guestCookieService = guestCookieService;
            _guestService = guestService;
        }
        [HttpGet]
        public async Task<IActionResult> Index(
            CancellationToken cancellationToken)
        {
            try
            {
                var guestToken = _guestCookieService.GetOrCreateToken();

                if (string.IsNullOrWhiteSpace(guestToken))
                {
                    ViewBag.HasGuestCookie = false;
                    return View();
                }

                var guest = await _guestService.GetGuestByTokenAsync(
                    guestToken,
                    cancellationToken);

                if (guest is null)
                {
                    _guestCookieService.DeleteToken();

                    ViewBag.HasGuestCookie = false;
                    return View(model: null);
                }

                ViewBag.HasGuestCookie = true;
                return View(guest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "เกิดข้อผิดพลาดขณะตรวจสอบ Guest Cookie");
                throw;
            }
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TryDemo(
            CancellationToken cancellationToken)
        {
            try
            {
                var guestToken = _guestCookieService.GetOrCreateToken();
                GuestResponseModel? guest = null;

                if (!string.IsNullOrWhiteSpace(guestToken))
                {
                    guest = await _guestService.GetGuestByTokenAsync(
                        guestToken,
                        cancellationToken);
                }

                if (guest is null)
                {
                    var createdGuest = await _guestService.GenerateGuestAsync(
                        cancellationToken);

                    _guestCookieService.SetToken(
                        createdGuest.GuestToken,
                        new DateTimeOffset(
                            createdGuest.Guest.ExpiresAtUtc,
                            TimeSpan.Zero));

                    guest = createdGuest.Guest;
                }

                return RedirectToAction("Index", "Boards");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "เกิดข้อผิดพลาดขณะทำงาน");
                throw;
            }
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
