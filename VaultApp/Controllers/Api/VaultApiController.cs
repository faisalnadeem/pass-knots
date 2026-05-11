using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using VaultApp.Models;
using VaultApp.Models.Api;
using VaultApp.Services;

namespace VaultApp.Controllers.Api;

[ApiController]
[Route("api/vault")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class VaultApiController : ControllerBase
{
    private readonly IVaultService _vault;
    private readonly UserManager<ApplicationUser> _userManager;

    public VaultApiController(IVaultService vault, UserManager<ApplicationUser> userManager)
    {
        _vault = vault;
        _userManager = userManager;
    }

    private string UserId => _userManager.GetUserId(User)!;

    private bool TryGetEncryptionKey(string? headerValue, out IActionResult? error)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            error = BadRequest(new
            {
                error = $"Missing or empty '{ApiConstants.EncryptionKeyHeader}' header."
            });
            return false;
        }

        error = null;
        return true;
    }

    private static VaultEntryDto Map(DecryptedVaultEntry d)
    {
        var emails = d.Entry.SharedWith?
            .Where(s => !string.IsNullOrWhiteSpace(s.SharedWithUser?.Email))
            .Select(s => s.SharedWithUser!.Email!)
            .Distinct()
            .OrderBy(e => e)
            .ToList() ?? new List<string>();

        return new VaultEntryDto
        {
            Id = d.Entry.Id,
            SiteName = d.Entry.SiteName,
            SiteUrl = d.Entry.SiteUrl,
            Username = d.Entry.Username,
            Password = d.PlainPassword,
            Notes = d.Entry.Notes,
            CreatedAt = d.Entry.CreatedAt,
            UpdatedAt = d.Entry.UpdatedAt,
            IsShared = d.IsShared,
            SharedWithEmails = emails
        };
    }

    /// <summary>List your entries and entries shared with you. Requires JWT + encryption key header.</summary>
    [HttpGet("entries")]
    [ProducesResponseType(typeof(VaultListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListEntries(
        [FromHeader(Name = ApiConstants.EncryptionKeyHeader)] string? encryptionKey)
    {
        if (!TryGetEncryptionKey(encryptionKey, out var bad)) return bad!;

        var own = await _vault.GetEntriesAsync(UserId, encryptionKey!);
        var shared = await _vault.GetSharedWithMeAsync(UserId, encryptionKey!);

        return Ok(new VaultListResponse
        {
            OwnEntries = own.Select(Map).ToList(),
            SharedWithMe = shared.Select(Map).ToList()
        });
    }

    /// <summary>Get a single entry (owned or shared with you).</summary>
    [HttpGet("entries/{id:int}")]
    [ProducesResponseType(typeof(VaultEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEntry(
        int id,
        [FromHeader(Name = ApiConstants.EncryptionKeyHeader)] string? encryptionKey)
    {
        if (!TryGetEncryptionKey(encryptionKey, out var bad)) return bad;

        var own = await _vault.GetEntryAsync(id, UserId, encryptionKey!);
        if (own is not null)
            return Ok(Map(own));

        var shared = await _vault.GetSharedWithMeAsync(UserId, encryptionKey!);
        var match = shared.FirstOrDefault(s => s.Entry.Id == id);
        if (match is not null)
            return Ok(Map(match));

        return NotFound();
    }

    /// <summary>Create a vault entry.</summary>
    [HttpPost("entries")]
    [ProducesResponseType(typeof(VaultEntryDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateEntry(
        [FromBody] VaultEntryCreateRequest model,
        [FromHeader(Name = ApiConstants.EncryptionKeyHeader)] string? encryptionKey)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetEncryptionKey(encryptionKey, out var bad)) return bad;

        var vm = new VaultEntryViewModel
        {
            SiteName = model.SiteName,
            SiteUrl = model.SiteUrl,
            Username = model.Username,
            Password = model.Password,
            Notes = model.Notes
        };

        var created = await _vault.CreateAsync(vm, UserId, encryptionKey!);
        var decrypted = await _vault.GetEntryAsync(created.Id, UserId, encryptionKey!);
        if (decrypted is null)
            return StatusCode(500, new { error = "Entry was created but could not be loaded." });

        return CreatedAtAction(nameof(GetEntry), new { id = created.Id }, Map(decrypted));
    }

    /// <summary>Update an entry you own.</summary>
    [HttpPut("entries/{id:int}")]
    [ProducesResponseType(typeof(VaultEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateEntry(
        int id,
        [FromBody] VaultEntryUpdateRequest model,
        [FromHeader(Name = ApiConstants.EncryptionKeyHeader)] string? encryptionKey)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetEncryptionKey(encryptionKey, out var bad)) return bad;

        var existing = await _vault.GetEntryAsync(id, UserId, encryptionKey!);
        if (existing is null)
            return NotFound();

        var vm = new VaultEntryViewModel
        {
            Id = id,
            SiteName = model.SiteName,
            SiteUrl = model.SiteUrl,
            Username = model.Username,
            Password = model.Password,
            Notes = model.Notes
        };

        await _vault.UpdateAsync(vm, UserId, encryptionKey!);
        var decrypted = await _vault.GetEntryAsync(id, UserId, encryptionKey!);
        if (decrypted is null)
            return NotFound();

        return Ok(Map(decrypted));
    }

    /// <summary>Delete an entry you own.</summary>
    [HttpDelete("entries/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteEntry(int id)
    {
        await _vault.DeleteAsync(id, UserId);
        return NoContent();
    }

    /// <summary>List users an entry is shared with (owner only).</summary>
    [HttpGet("entries/{id:int}/recipients")]
    [ProducesResponseType(typeof(IReadOnlyList<ShareRecipientDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRecipients(
        int id,
        [FromHeader(Name = ApiConstants.EncryptionKeyHeader)] string? encryptionKey)
    {
        if (!TryGetEncryptionKey(encryptionKey, out var bad)) return bad;

        var entry = await _vault.GetEntryAsync(id, UserId, encryptionKey!);
        if (entry is null)
            return NotFound();

        var recipients = await _vault.GetSharedRecipientsAsync(id, UserId);
        return Ok(recipients.Select(r => new ShareRecipientDto
        {
            SharedEntryId = r.SharedEntryId,
            Email = r.Email
        }).ToList());
    }

    /// <summary>Share an entry with another user by email.</summary>
    [HttpPost("entries/{id:int}/share")]
    [ProducesResponseType(typeof(ShareEntryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ShareEntry(
        int id,
        [FromBody] ShareEntryRequest model,
        [FromHeader(Name = ApiConstants.EncryptionKeyHeader)] string? encryptionKey)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetEncryptionKey(encryptionKey, out var bad)) return bad;

        var (ok, error, pendingInvite) = await _vault.ShareAsync(id, UserId, encryptionKey!, model.RecipientEmail);
        if (!ok)
            return BadRequest(new { error });

        var message = pendingInvite
            ? "This entry has been shared. It will be visible once the recipient approves via email."
            : $"Entry shared with {model.RecipientEmail}.";

        return Ok(new ShareEntryResponse { PendingInvite = pendingInvite, Message = message });
    }

    /// <summary>Remove sharing for a recipient.</summary>
    [HttpDelete("entries/{vaultEntryId:int}/shares/{sharedEntryId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Unshare(int vaultEntryId, int sharedEntryId)
    {
        var removed = await _vault.UnshareAsync(sharedEntryId, UserId);
        if (!removed)
            return BadRequest(new { error = "Unable to remove sharing." });
        return NoContent();
    }
}
