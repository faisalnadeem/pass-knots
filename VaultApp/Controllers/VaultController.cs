using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using VaultApp.Models;
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

    // ── Index ─────────────────────────────────────────────────────────────────
    public async Task<IActionResult> Index()
    {
        if (EncKey is null) return RequireKey();
        var own    = await _vault.GetEntriesAsync(UserId, EncKey);
        var shared = await _vault.GetSharedWithMeAsync(UserId, EncKey);
        ViewBag.Shared = shared;
        return View(own);
    }

    // ── Create ────────────────────────────────────────────────────────────────
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

    // ── Edit ──────────────────────────────────────────────────────────────────
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

    // ── Delete ────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        await _vault.DeleteAsync(id, UserId);
        TempData["Success"] = "Entry deleted.";
        return RedirectToAction(nameof(Index));
    }

    // ── Share ─────────────────────────────────────────────────────────────────
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
            SharedWith = await _vault.GetSharedRecipientsAsync(id, UserId)
        };
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Share(ShareViewModel model)
    {
        model.SharedWith = await _vault.GetSharedRecipientsAsync(model.VaultEntryId, UserId);
        if (!ModelState.IsValid) return View(model);
        if (EncKey is null) return RequireKey();

        var (ok, error, pendingInvite) = await _vault.ShareAsync(
            model.VaultEntryId, UserId, EncKey, model.RecipientEmail);

        if (!ok)
        {
            ModelState.AddModelError("", error);
            model.SharedWith = await _vault.GetSharedRecipientsAsync(model.VaultEntryId, UserId);
            return View(model);
        }

        TempData["Success"] = pendingInvite
            ? "This entry has been shared with the user. It will be visible once they approve the request through the email sent to them."
            : $"Entry shared with {model.RecipientEmail}.";
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
}
