using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using TaskManager.WebUI.Models;

namespace TaskManager.WebUI.Controllers
{
    public class HomeController : Controller
    {
        public static readonly string Name = "Home";
        public static readonly string ActionIndex = "Index";
        public static readonly string ActionTryDemo = "TryDemo";

        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            try
            {
                // ทำงาน
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "เกิดข้อผิดพลาดขณะทำงาน");
                throw;
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult TryDemo()
        {
            try
            {
                // ทำงาน
                return NoContent();
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
