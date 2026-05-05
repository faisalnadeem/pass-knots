using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using VaultApp.Models;
using VaultApp.Services;

namespace VaultApp.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser>   _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IEncryptionService             _enc;

    public AccountController(
        UserManager<ApplicationUser>   userManager,
        SignInManager<ApplicationUser> signInManager,
        IEncryptionService             enc)
    {
        _userManager   = userManager;
        _signInManager = signInManager;
        _enc           = enc;
    }

    // ── Register ──────────────────────────────────────────────────────────────
    [HttpGet] public IActionResult Register() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = new ApplicationUser
        {
            UserName           = model.Email,
            Email              = model.Email,
            EncryptionKeyHash  = _enc.HashEncryptionKey(model.EncryptionKey)
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            await _signInManager.SignInAsync(user, isPersistent: false);
            // Store encryption key in session (never persisted to DB in plaintext)
            HttpContext.Session.SetString("EncKey", model.EncryptionKey);
            return RedirectToAction("Index", "Vault");
        }

        foreach (var e in result.Errors)
            ModelState.AddModelError("", e.Description);

        return View(model);
    }

    // ── Login ─────────────────────────────────────────────────────────────────
    [HttpGet] public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is null)
        {
            ModelState.AddModelError("", "Invalid credentials.");
            return View(model);
        }

        // Verify encryption key BEFORE signing in
        if (string.IsNullOrEmpty(user.EncryptionKeyHash) ||
            !_enc.VerifyEncryptionKey(model.EncryptionKey, user.EncryptionKeyHash))
        {
            ModelState.AddModelError("", "Invalid credentials.");
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            HttpContext.Session.SetString("EncKey", model.EncryptionKey);
            return LocalRedirect(returnUrl ?? "/Vault");
        }
        if (result.IsLockedOut)
        {
            ModelState.AddModelError("", "Account locked out. Try again later.");
            return View(model);
        }

        ModelState.AddModelError("", "Invalid credentials.");
        return View(model);
    }

    // ── Logout ────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        HttpContext.Session.Clear();
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }
}
