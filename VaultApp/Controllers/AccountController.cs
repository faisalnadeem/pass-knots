using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using VaultApp.Models;
using VaultApp.Services;

namespace VaultApp.Controllers;

public class AccountController : Controller
{
    private const string EncKeyCookieName = "VaultApp.EncKey";
    private readonly ILogger<AccountController>      _logger;
    private readonly UserManager<ApplicationUser>   _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IEncryptionService             _enc;
    private readonly IVaultService                  _vault;
    private readonly IDataProtector                 _encKeyProtector;

    public AccountController(
        ILogger<AccountController>       logger,
        UserManager<ApplicationUser>   userManager,
        SignInManager<ApplicationUser> signInManager,
        IEncryptionService             enc,
        IVaultService                  vault,
        IDataProtectionProvider        dataProtectionProvider)
    {
        _logger        = logger;
        _userManager   = userManager;
        _signInManager = signInManager;
        _enc           = enc;
        _vault         = vault;
        _encKeyProtector = dataProtectionProvider.CreateProtector("VaultApp.EncKeyCookie.v1");
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
            WriteEncKeyCookie(model.EncryptionKey, false);
            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                try
                {
                    await _vault.ClaimPendingSharesAsync(user.Id, user.Email);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to claim pending shares during register for user {UserId}", user.Id);
                }
            }
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
            WriteEncKeyCookie(model.EncryptionKey, model.RememberMe);
            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                try
                {
                    await _vault.ClaimPendingSharesAsync(user.Id, user.Email);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to claim pending shares during login for user {UserId}", user.Id);
                }
            }
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
        Response.Cookies.Delete(EncKeyCookieName);
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    private void WriteEncKeyCookie(string encKey, bool isPersistent)
    {
        var protectedValue = _encKeyProtector.Protect(encKey);
        var options = new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = HttpContext.Request.IsHttps
        };

        if (isPersistent)
        {
            options.Expires = DateTimeOffset.UtcNow.AddDays(30);
        }

        Response.Cookies.Append(EncKeyCookieName, protectedValue, options);
    }
}
