using System.ComponentModel.DataAnnotations;

namespace VaultApp.Models.Api;

public static class ApiConstants
{
    public const string EncryptionKeyHeader = "X-Encryption-Key";
}

public class ApiRegisterRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, MinLength(8)]
    public string Password { get; set; } = "";

    [Required, MinLength(12)]
    public string EncryptionKey { get; set; } = "";
}

public class ApiLoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";

    [Required]
    public string EncryptionKey { get; set; } = "";
}

public class AuthTokenResponse
{
    public string Token { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public string Email { get; set; } = "";
    public string UserId { get; set; } = "";
}

public class VaultEntryDto
{
    public int Id { get; set; }
    public string SiteName { get; set; } = "";
    public string SiteUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsShared { get; set; }
    public List<string> SharedWithEmails { get; set; } = new();
}

public class VaultListResponse
{
    public List<VaultEntryDto> OwnEntries { get; set; } = new();
    public List<VaultEntryDto> SharedWithMe { get; set; } = new();
}

public class VaultEntryCreateRequest
{
    [Required, MaxLength(100)]
    public string SiteName { get; set; } = "";

    [MaxLength(200)]
    public string? SiteUrl { get; set; }

    [Required, MaxLength(200)]
    public string Username { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";

    [MaxLength(500)]
    public string? Notes { get; set; }
}

public class VaultEntryUpdateRequest
{
    [Required, MaxLength(100)]
    public string SiteName { get; set; } = "";

    [MaxLength(200)]
    public string? SiteUrl { get; set; }

    [Required, MaxLength(200)]
    public string Username { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";

    [MaxLength(500)]
    public string? Notes { get; set; }
}

public class ShareEntryRequest
{
    [Required, EmailAddress]
    public string RecipientEmail { get; set; } = "";
}

public class ShareEntryResponse
{
    public bool PendingInvite { get; set; }
    public string Message { get; set; } = "";
}

public class ShareRecipientDto
{
    public int SharedEntryId { get; set; }
    public string Email { get; set; } = "";
}

public class GenerateShareCodeResponse
{
    public string ShareCode { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}

public class RedeemShareCodeRequest
{
    [Required, MaxLength(32)]
    public string ShareCode { get; set; } = "";
}
