using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using VaultApp.Models;
using VaultApp.Models.Api;
using VaultApp.Services;

namespace VaultApp.Controllers;

[Authorize]
public class VaultController : Controller
{
    private readonly IVaultService                  _vault;
    private readonly UserManager<ApplicationUser>   _userManager;

    public VaultController(IVaultService vault, UserManager<ApplicationUser> userManager)
    {
        _vault       = vault;
        _userManager = userManager;
    }

    private string UserId    => _userManager.GetUserId(User)!;
    private string? EncKey   => HttpContext.Session.GetString("EncKey");

    private IActionResult RequireKey()
    {
        TempData["Error"] = "Session expired. Please log in again.";
        return RedirectToAction("Login", "Account");
    }

    public async Task<IActionResult> Index()
    {
        if (EncKey is null) return RequireKey();
        var own    = await _vault.GetEntriesAsync(UserId, EncKey);
        var shared = await _vault.GetSharedWithMeAsync(UserId, EncKey);
        ViewBag.Shared = shared;
        return View(own);
    }

    [HttpGet] public IActionResult Create() => View(new VaultEntryViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(VaultEntryViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        if (EncKey is null) return RequireKey();
        await _vault.CreateAsync(model, UserId, EncKey);
        TempData["Success"] = $"Entry for '{model.SiteName}' saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        if (EncKey is null) return RequireKey();
        var entry = await _vault.GetEntryAsync(id, UserId, EncKey);
        if (entry is null) return NotFound();
        var vm = new VaultEntryViewModel
        {
            Id       = entry.Entry.Id,
            SiteName = entry.Entry.SiteName,
            SiteUrl  = entry.Entry.SiteUrl,
            Username = entry.Entry.Username,
            Password = entry.PlainPassword,
            Notes    = entry.Entry.Notes
        };
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(VaultEntryViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        if (EncKey is null) return RequireKey();
        await _vault.UpdateAsync(model, UserId, EncKey);
        TempData["Success"] = $"Entry for '{model.SiteName}' updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        await _vault.DeleteAsync(id, UserId);
        TempData["Success"] = "Entry deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Share(int id)
    {
        if (EncKey is null) return RequireKey();
        var entry = await _vault.GetEntryAsync(id, UserId, EncKey);
        if (entry is null) return NotFound();
        var model = new ShareViewModel
        {
            VaultEntryId = id,
            SiteName = entry.Entry.SiteName,
            SharedWith = await _vault.GetSharedRecipientsAsync(id, UserId),
            ActiveShareCode = TempData["ShareCodeDisplay"] as string,
            ActiveShareCodeExpiresAt = TempData["ShareCodeExpiresAt"] is string exp
                && DateTime.TryParse(exp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : null
        };
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Share(ShareViewModel model)
    {
        model.SharedWith = await _vault.GetSharedRecipientsAsync(model.VaultEntryId, UserId);
        if (EncKey is null) return RequireKey();

        if (string.IsNullOrWhiteSpace(model.RecipientEmail))
        {
            ModelState.AddModelError(nameof(model.RecipientEmail), "Recipient email is required.");
            return View(model);
        }

        if (!ModelState.IsValid)
            return View(model);

        var (ok, error, pendingInvite) = await _vault.ShareAsync(
            model.VaultEntryId, UserId, EncKey, model.RecipientEmail);

        if (!ok)
        {
            ModelState.AddModelError("", error);
            model.SharedWith = await _vault.GetSharedRecipientsAsync(model.VaultEntryId, UserId);
            return View(model);
        }

        TempData["Success"] = ShareEntryResponse.GetSuccessMessage(pendingInvite, model.RecipientEmail);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unshare(int vaultEntryId, int sharedEntryId)
    {
        var removed = await _vault.UnshareAsync(sharedEntryId, UserId);
        TempData[removed ? "Success" : "Error"] = removed
            ? "Sharing removed."
            : "Unable to remove sharing.";
        return RedirectToAction(nameof(Share), new { id = vaultEntryId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateShareCode(int vaultEntryId)
    {
        if (EncKey is null) return RequireKey();

        var (ok, error, shareCode, expiresAt) = await _vault.GenerateShareCodeAsync(vaultEntryId, UserId, EncKey);
        if (ok)
        {
            TempData["ShareCodeDisplay"] = shareCode;
            if (expiresAt.HasValue)
                TempData["ShareCodeExpiresAt"] = expiresAt.Value.ToString("o");
            TempData["Success"] = "Share code generated. Copy it before you leave this page.";
        }
        else
        {
            TempData["Error"] = error;
        }

        return RedirectToAction(nameof(Share), new { id = vaultEntryId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RedeemShareCode(RedeemShareCodeViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Please enter a valid share code.";
            return RedirectToAction(nameof(Index));
        }

        if (EncKey is null) return RequireKey();

        var (ok, error) = await _vault.RedeemShareCodeAsync(model.ShareCode, UserId);
        TempData[ok ? "Success" : "Error"] = ok
            ? "Entry added to Shared with me."
            : error;
        return RedirectToAction(nameof(Index));
    }
}
