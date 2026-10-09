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
        private readonly BoardOwnerCookieService _boardOwnerCookieService;
        private readonly IGuestService _guestService;
        private readonly IBoardService _boardService;

        public HomeController(
            ILogger<HomeController> logger,
            GuestCookieService guestCookieService,
            BoardOwnerCookieService boardOwnerCookieService,
            IGuestService guestService,
            IBoardService boardService)
        {
            _logger = logger;
            _guestCookieService = guestCookieService;
            _boardOwnerCookieService = boardOwnerCookieService;
            _guestService = guestService;
            _boardService = boardService;
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
                    return View(new HomePageViewModel(null, []));
                }

                var guest = await _guestService.GetGuestByTokenAsync(
                    guestToken,
                    cancellationToken);

                if (guest is null)
                {
                    _guestCookieService.DeleteToken();

                    ViewBag.HasGuestCookie = false;
                    return View(new HomePageViewModel(null, []));
                }

                var boards = await _boardService.GetListBySessionIdAsync(
                    guest.Id,
                    cancellationToken);

                ViewBag.HasGuestCookie = true;
                return View(new HomePageViewModel(guest, boards));
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

                var boards = await _boardService.GetListBySessionIdAsync(
                    guest.Id,
                    cancellationToken);

                if (boards.Count > 0)
                {
                    return RedirectToAction(
                        "Index",
                        "Boards",
                        new { publicId = boards[0].PublicId });
                }

                var createdBoard = await _boardService.CreateAsync(
                    new CreateBoardRequestModel(
                        guest.Id,
                        "Website Launch",
                        "วางแผนเปิดตัวเว็บไซต์",
                        guest.ExpiresAtUtc,
                        IncludeSampleData: true),
                    cancellationToken);

                _boardOwnerCookieService.SetToken(
                    createdBoard.Board.PublicId,
                    createdBoard.OwnerToken,
                    createdBoard.Board.ExpiresAtUtc);

                return RedirectToAction(
                    "Index",
                    "Boards",
                    new { publicId = createdBoard.Board.PublicId });
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
