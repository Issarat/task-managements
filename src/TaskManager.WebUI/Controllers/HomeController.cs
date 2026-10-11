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
            string? boardPublicId,
            CancellationToken cancellationToken)
        {
            try
            {
                var sharedBoard = boardPublicId is null ? null
                    : await _boardService.GetPreviewAsync(boardPublicId, cancellationToken);
                HomePageViewModel CreatePage(GuestResponseModel? guest,
                    IReadOnlyList<BoardNavigationModel> boards) => new(guest, boards)
                    {
                        SharedBoard = sharedBoard,
                        SharedBoardUnavailable = boardPublicId is not null && sharedBoard is null
                    };
                var guestToken = _guestCookieService.GetOrCreateToken();

                if (string.IsNullOrWhiteSpace(guestToken))
                {
                    ViewBag.HasGuestCookie = false;
                    return View(CreatePage(null, []));
                }

                var guest = await _guestService.GetGuestByTokenAsync(
                    guestToken,
                    cancellationToken);

                if (guest is null)
                {
                    _guestCookieService.DeleteToken();

                    ViewBag.HasGuestCookie = false;
                    return View(CreatePage(null, []));
                }

                var boards = await _boardService.GetListBySessionIdAsync(
                    guest.Id,
                    cancellationToken);

                ViewBag.HasGuestCookie = true;
                return View(CreatePage(guest, boards));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "เกิดข้อผิดพลาดขณะตรวจสอบ Guest Cookie");
                throw;
            }
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> JoinBoard(string? publicId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(publicId))
                return BadRequest();

            // Validate the board before creating a guest; never accept an external return URL.
            var preview = await _boardService.GetPreviewAsync(publicId, cancellationToken);
            if (preview is null)
                return RedirectToAction(nameof(Index), new { boardPublicId = publicId });

            var guest = await GetOrCreateGuestAsync(cancellationToken);
            var board = await _boardService.GetAsync(publicId, guest.Id, cancellationToken);
            if (board is null)
                return RedirectToAction(nameof(Index), new { boardPublicId = publicId });

            // Joining never sets the owner cookie or seeds another board.
            return RedirectToAction("Index", "Boards", new { publicId = board.Board.PublicId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TryDemo(
            CancellationToken cancellationToken)
        {
            try
            {
                var guest = await GetOrCreateGuestAsync(cancellationToken);

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

        private async Task<GuestResponseModel> GetOrCreateGuestAsync(CancellationToken cancellationToken)
        {
            var token = _guestCookieService.GetOrCreateToken();
            var guest = string.IsNullOrWhiteSpace(token) ? null
                : await _guestService.GetGuestByTokenAsync(token, cancellationToken);
            if (guest is not null)
                return guest;

            var created = await _guestService.GenerateGuestAsync(cancellationToken);
            _guestCookieService.SetToken(created.GuestToken,
                new DateTimeOffset(created.Guest.ExpiresAtUtc, TimeSpan.Zero));
            return created.Guest;
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
