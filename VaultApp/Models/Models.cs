using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace VaultApp.Models;

// ── Identity user ─────────────────────────────────────────────────────────────
public class ApplicationUser : IdentityUser
{
    // Salted hash of the user's encryption key (never store the key itself)
    public string? EncryptionKeyHash { get; set; }
}

// ── Password entry stored in the vault ───────────────────────────────────────
public class VaultEntry
{
    public int Id { get; set; }

    [Required] public string OwnerId { get; set; } = "";
    public ApplicationUser? Owner { get; set; }

    [Required, MaxLength(100)] public string SiteName   { get; set; } = "";
    [MaxLength(200)]           public string SiteUrl    { get; set; } = "";
    [Required, MaxLength(200)] public string Username   { get; set; } = "";

    // AES-256-GCM ciphertext (Base64)
    [Required] public string EncryptedPassword { get; set; } = "";
    // Per-entry random IV (Base64)
    [Required] public string IV                { get; set; } = "";

    [MaxLength(500)] public string Notes { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SharedEntry> SharedWith { get; set; } = new List<SharedEntry>();
}

// ── Sharing record ────────────────────────────────────────────────────────────
public class SharedEntry
{
    public int Id { get; set; }

    public int        VaultEntryId { get; set; }
    public VaultEntry? VaultEntry  { get; set; }

    public string  SharedWithUserId { get; set; } = "";
    public ApplicationUser? SharedWithUser { get; set; }

    // The entry's AES key re-encrypted with the recipient's derived key
    public string ReEncryptedKey { get; set; } = "";
    public string ReEncryptedIV  { get; set; } = "";

    public DateTime SharedAt { get; set; } = DateTime.UtcNow;
}

// ── View-models ───────────────────────────────────────────────────────────────
public class RegisterViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password), MinLength(8)]
    public string Password { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(Password))]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = "";

    [Required, MinLength(12)]
    [Display(Name = "Encryption Key")]
    public string EncryptionKey { get; set; } = "";

    [Required, Compare(nameof(EncryptionKey))]
    [Display(Name = "Confirm Encryption Key")]
    public string ConfirmEncryptionKey { get; set; } = "";
}

public class LoginViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Required]
    [Display(Name = "Encryption Key")]
    public string EncryptionKey { get; set; } = "";

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }
}

public class VaultEntryViewModel
{
    public int    Id        { get; set; }

    [Required, MaxLength(100)]
    [Display(Name = "Site / App Name")]
    public string SiteName { get; set; } = "";

    [MaxLength(200), Url]
    [Display(Name = "Site URL (optional)")]
    public string? SiteUrl { get; set; }

    [Required, MaxLength(200)]
    public string Username { get; set; } = "";

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [MaxLength(500)]
    public string Notes { get; set; } = "";
}

public class ShareViewModel
{
    public int    VaultEntryId   { get; set; }
    public string SiteName       { get; set; } = "";

    [Required, EmailAddress]
    [Display(Name = "Recipient Email")]
    public string RecipientEmail { get; set; } = "";
}

public class DecryptedVaultEntry
{
    public VaultEntry Entry          { get; set; } = null!;
    public string     PlainPassword  { get; set; } = "";
    public bool       IsShared       { get; set; }   // came from a SharedEntry
}
