using Microsoft.AspNetCore.Mvc;

namespace VaultApp.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => View();
}
