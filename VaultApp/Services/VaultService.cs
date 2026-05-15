using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using VaultApp.Data;
using VaultApp.Models;

namespace VaultApp.Services;

public interface IVaultService
{
    Task<List<DecryptedVaultEntry>> GetEntriesAsync(string userId, string encKey);
    Task<DecryptedVaultEntry?>      GetEntryAsync(int id, string userId, string encKey);
    Task<VaultEntry>                CreateAsync(VaultEntryViewModel vm, string userId, string encKey);
    Task                            UpdateAsync(VaultEntryViewModel vm, string userId, string encKey);
    Task                            DeleteAsync(int id, string userId);
    Task<(bool ok, string error, bool pendingInvite)> ShareAsync(int entryId, string ownerId, string ownerKey, string recipientEmail);
    Task<(bool ok, string error, string? shareCode, DateTime? expiresAt)> GenerateShareCodeAsync(int entryId, string ownerId, string ownerKey);
    Task<(bool ok, string error)> RedeemShareCodeAsync(string shareCode, string consumerUserId);
    Task<(string? shareCode, DateTime? expiresAt)> GetActiveShareCodeAsync(int entryId, string ownerId);
    Task<List<DecryptedVaultEntry>> GetSharedWithMeAsync(string userId, string encKey);
    Task<List<ShareRecipientViewModel>> GetSharedRecipientsAsync(int entryId, string ownerId);
    Task<bool>                      UnshareAsync(int sharedEntryId, string ownerId);
    Task ClaimPendingSharesAsync(string userId, string email);
}

public class VaultService : IVaultService
{
    private readonly ApplicationDbContext _db;
    private readonly IEncryptionService   _enc;
    private readonly IShareInviteQueue    _inviteQueue;
    private readonly string               _shareSecret;
    private readonly string               _baseUrl;
    private static readonly TimeSpan ShareCodeLifetime = TimeSpan.FromDays(30);

    public VaultService(
        ApplicationDbContext db,
        IEncryptionService enc,
        IShareInviteQueue inviteQueue,
        IConfiguration config)
    {
        _db  = db;
        _enc = enc;
        _inviteQueue = inviteQueue;
        _shareSecret = config["Security:ShareKey"] ?? "VaultApp-Share-Key-Change-In-Production";
        _baseUrl = config["App:BaseUrl"] ?? "https://localhost:5001";
    }

    public async Task<List<DecryptedVaultEntry>> GetEntriesAsync(string userId, string encKey)
    {
        var entries = await _db.VaultEntries
            .Include(e => e.SharedWith)
            .ThenInclude(s => s.SharedWithUser)
            .Where(e => e.OwnerId == userId)
            .OrderByDescending(e => e.UpdatedAt)
            .ToListAsync();

        return entries.Select(e => new DecryptedVaultEntry
        {
            Entry         = e,
            PlainPassword = SafeDecrypt(e.EncryptedPassword, e.IV, encKey),
            IsShared      = false
        }).ToList();
    }

    public async Task<DecryptedVaultEntry?> GetEntryAsync(int id, string userId, string encKey)
    {
        var entry = await _db.VaultEntries
            .Include(e => e.SharedWith)
            .ThenInclude(s => s.SharedWithUser)
            .FirstOrDefaultAsync(e => e.Id == id && e.OwnerId == userId);

        if (entry is null) return null;
        return new DecryptedVaultEntry
        {
            Entry         = entry,
            PlainPassword = SafeDecrypt(entry.EncryptedPassword, entry.IV, encKey),
            IsShared      = false
        };
    }

    public async Task<VaultEntry> CreateAsync(VaultEntryViewModel vm, string userId, string encKey)
    {
        var (cipher, iv) = _enc.Encrypt(vm.Password, encKey);
        var entry = new VaultEntry
        {
            OwnerId           = userId,
            SiteName          = vm.SiteName,
            SiteUrl           = vm.SiteUrl ?? "",
            Username          = vm.Username,
            EncryptedPassword = cipher,
            IV                = iv,
            Notes             = vm.Notes ?? "",
            CreatedAt         = DateTime.UtcNow,
            UpdatedAt         = DateTime.UtcNow
        };
        _db.VaultEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    public async Task UpdateAsync(VaultEntryViewModel vm, string userId, string encKey)
    {
        var entry = await _db.VaultEntries
            .FirstOrDefaultAsync(e => e.Id == vm.Id && e.OwnerId == userId)
            ?? throw new InvalidOperationException("Entry not found.");

        var (cipher, iv)  = _enc.Encrypt(vm.Password, encKey);
        entry.SiteName          = vm.SiteName;
        entry.SiteUrl           = vm.SiteUrl ?? "";
        entry.Username          = vm.Username;
        entry.EncryptedPassword = cipher;
        entry.IV                = iv;
        entry.Notes             = vm.Notes ?? "";
        entry.UpdatedAt         = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id, string userId)
    {
        var entry = await _db.VaultEntries
            .FirstOrDefaultAsync(e => e.Id == id && e.OwnerId == userId);
        if (entry is null) return;
        _db.VaultEntries.Remove(entry);
        await _db.SaveChangesAsync();
    }

    public async Task<(bool ok, string error, bool pendingInvite)> ShareAsync(
        int entryId, string ownerId, string ownerKey, string recipientEmail)
    {
        var entry = await _db.VaultEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.OwnerId == ownerId);
        if (entry is null) return (false, "Entry not found.", false);

        var normalizedEmail = (recipientEmail ?? "").Trim().ToLowerInvariant();
        var owner = await _db.Users.FirstOrDefaultAsync(u => u.Id == ownerId);
        var recipient = await _db.Users.FirstOrDefaultAsync(
            u => u.Email != null && u.Email.ToLower() == normalizedEmail);

        string plainPwd = _enc.Decrypt(entry.EncryptedPassword, entry.IV, ownerKey);
        if (recipient is null)
        {
            // Queue pending share for this email and invite recipient to sign up.
            var (pendingCipher, pendingIv) = _enc.Encrypt(plainPwd, BuildPendingShareKey(normalizedEmail));
            var existingPending = await _db.PendingShares.FirstOrDefaultAsync(
                p => p.VaultEntryId == entryId
                     && p.RecipientEmail == normalizedEmail
                     && p.ShareCode == null);

            if (existingPending is null)
            {
                _db.PendingShares.Add(new PendingShare
                {
                    VaultEntryId = entryId,
                    UserId = ownerId,
                    RecipientEmail = normalizedEmail,
                    ShareCode = null,
                    EncryptedPassword = pendingCipher,
                    IV = pendingIv,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.Add(ShareCodeLifetime)
                });
            }
            else
            {
                existingPending.UserId = ownerId;
                existingPending.EncryptedPassword = pendingCipher;
                existingPending.IV = pendingIv;
                existingPending.CreatedAt = DateTime.UtcNow;
                existingPending.ExpiresAt = DateTime.UtcNow.Add(ShareCodeLifetime);
            }

            await _db.SaveChangesAsync();
            var signupLink = $"{_baseUrl.TrimEnd('/')}/Account/Register";
            await _inviteQueue.EnqueueAsync(new ShareInviteMessage(
                normalizedEmail,
                owner?.Email ?? "A PassKnots user",
                entry.SiteName,
                signupLink));
            return (true, "", true);
        }

        if (recipient.Id == ownerId) return (false, "You cannot share with yourself.", false);
        var (shareCipher, shareIv) = _enc.Encrypt(plainPwd, BuildShareKey(recipient.Id));

        // If a share already exists, refresh it so legacy/non-decryptable records get repaired.
        var existingShares = await _db.SharedEntries
            .Where(s => s.VaultEntryId == entryId && s.SharedWithUserId == recipient.Id)
            .OrderByDescending(s => s.SharedAt)
            .ToListAsync();

        if (existingShares.Count == 0)
        {
            var share = new SharedEntry
            {
                VaultEntryId      = entryId,
                SharedWithUserId  = recipient.Id,
                ReEncryptedKey    = shareCipher,
                ReEncryptedIV     = shareIv,
                SharedAt          = DateTime.UtcNow
            };
            _db.SharedEntries.Add(share);
        }
        else
        {
            var latestShare = existingShares[0];
            latestShare.ReEncryptedKey = shareCipher;
            latestShare.ReEncryptedIV  = shareIv;
            latestShare.SharedAt       = DateTime.UtcNow;

            // Keep one record per entry+recipient and drop older duplicates.
            if (existingShares.Count > 1)
            {
                _db.SharedEntries.RemoveRange(existingShares.Skip(1));
            }
        }

        await _db.SaveChangesAsync();
        return (true, "", false);
    }

    public async Task<(bool ok, string error, string? shareCode, DateTime? expiresAt)> GenerateShareCodeAsync(
        int entryId, string ownerId, string ownerKey)
    {
        var entry = await _db.VaultEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.OwnerId == ownerId);
        if (entry is null) return (false, "Entry not found.", null, null);

        var plainPwd = _enc.Decrypt(entry.EncryptedPassword, entry.IV, ownerKey);
        var code = await CreateUniqueShareCodeAsync();
        var (pendingCipher, pendingIv) = _enc.Encrypt(plainPwd, BuildPendingShareKey($"code:{code}"));
        var now = DateTime.UtcNow;
        var expiresAt = now.Add(ShareCodeLifetime);

        var existing = await _db.PendingShares
            .Where(p => p.VaultEntryId == entryId
                        && p.ShareCode != null
                        && !p.IsConsumed
                        && p.ExpiresAt > now)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        if (existing is null)
        {
            _db.PendingShares.Add(new PendingShare
            {
                VaultEntryId = entryId,
                UserId = ownerId,
                RecipientEmail = null,
                ShareCode = code,
                EncryptedPassword = pendingCipher,
                IV = pendingIv,
                CreatedAt = now,
                ExpiresAt = expiresAt,
                IsConsumed = false
            });
        }
        else
        {
            existing.UserId = ownerId;
            existing.ShareCode = code;
            existing.RecipientEmail = null;
            existing.EncryptedPassword = pendingCipher;
            existing.IV = pendingIv;
            existing.CreatedAt = now;
            existing.ExpiresAt = expiresAt;
            existing.IsConsumed = false;
            existing.SharedWithUserId = null;
        }

        await _db.SaveChangesAsync();
        return (true, "", code, expiresAt);
    }

    public async Task<(bool ok, string error)> RedeemShareCodeAsync(string shareCode, string consumerUserId)
    {
        var normalizedCode = NormalizeShareCode(shareCode);
        if (string.IsNullOrEmpty(normalizedCode))
            return (false, "Share code is required.");

        var pending = await _db.PendingShares
            .Include(p => p.VaultEntry)
            .FirstOrDefaultAsync(p => p.ShareCode == normalizedCode);

        if (pending is null || pending.ShareCode is null)
            return (false, "Invalid share code.");

        if (pending.IsConsumed)
            return (false, "This share code has already been used.");

        if (pending.ExpiresAt <= DateTime.UtcNow)
            return (false, "This share code has expired.");

        if (pending.UserId == consumerUserId)
            return (false, "You cannot redeem a share code for your own entry.");

        if (pending.VaultEntry is null)
            return (false, "Entry no longer exists.");

        var alreadyShared = await _db.SharedEntries.AnyAsync(s =>
            s.VaultEntryId == pending.VaultEntryId && s.SharedWithUserId == consumerUserId);
        if (alreadyShared)
            return (false, "This entry is already in your vault.");

        var plain = SafeDecrypt(
            pending.EncryptedPassword,
            pending.IV,
            BuildPendingShareKey($"code:{normalizedCode}"));
        if (plain.StartsWith("[Decryption failed", StringComparison.Ordinal))
            return (false, "Unable to process this share code.");

        var (shareCipher, shareIv) = _enc.Encrypt(plain, BuildShareKey(consumerUserId));
        _db.SharedEntries.Add(new SharedEntry
        {
            VaultEntryId = pending.VaultEntryId,
            SharedWithUserId = consumerUserId,
            ReEncryptedKey = shareCipher,
            ReEncryptedIV = shareIv,
            SharedAt = DateTime.UtcNow
        });

        pending.IsConsumed = true;
        pending.SharedWithUserId = consumerUserId;
        await _db.SaveChangesAsync();
        return (true, "");
    }

    public async Task<(string? shareCode, DateTime? expiresAt)> GetActiveShareCodeAsync(int entryId, string ownerId)
    {
        var now = DateTime.UtcNow;
        var pending = await _db.PendingShares
            .Where(p => p.VaultEntryId == entryId
                        && p.UserId == ownerId
                        && p.ShareCode != null
                        && !p.IsConsumed
                        && p.ExpiresAt > now)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        if (pending is null) return (null, null);
        return (pending.ShareCode, pending.ExpiresAt);
    }

    public async Task<List<DecryptedVaultEntry>> GetSharedWithMeAsync(string userId, string encKey)
    {
        var shared = await _db.SharedEntries
            .Include(s => s.VaultEntry)
            .ThenInclude(v => v!.Owner)
            .Where(s => s.SharedWithUserId == userId)
            .OrderByDescending(s => s.SharedAt)
            .ToListAsync();

        var latestPerEntry = shared
            .GroupBy(s => s.VaultEntryId)
            .Select(g => g.OrderByDescending(x => x.SharedAt).First())
            .ToList();

        return latestPerEntry.Select(s => new DecryptedVaultEntry
        {
            Entry         = s.VaultEntry!,
            PlainPassword = SafeDecryptShared(s.ReEncryptedKey, s.ReEncryptedIV, userId),
            IsShared      = true
        }).ToList();
    }

    public async Task<List<ShareRecipientViewModel>> GetSharedRecipientsAsync(int entryId, string ownerId)
    {
        var shares = await _db.SharedEntries
            .Include(s => s.VaultEntry)
            .Include(s => s.SharedWithUser)
            .Where(s => s.VaultEntryId == entryId && s.VaultEntry!.OwnerId == ownerId)
            .ToListAsync();

        return shares
            .GroupBy(s => s.SharedWithUserId)
            .Select(g => g.OrderByDescending(x => x.SharedAt).First())
            .Where(s => !string.IsNullOrWhiteSpace(s.SharedWithUser!.Email))
            .OrderBy(s => s.SharedWithUser!.Email)
            .Select(s => new ShareRecipientViewModel
            {
                SharedEntryId = s.Id,
                Email = s.SharedWithUser!.Email!
            })
            .ToList();
    }

    public async Task<bool> UnshareAsync(int sharedEntryId, string ownerId)
    {
        var share = await _db.SharedEntries
            .Include(s => s.VaultEntry)
            .FirstOrDefaultAsync(s => s.Id == sharedEntryId && s.VaultEntry!.OwnerId == ownerId);

        if (share is null) return false;

        _db.SharedEntries.Remove(share);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task ClaimPendingSharesAsync(string userId, string email)
    {
        var normalizedEmail = (email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail)) return;

        var pendingShares = await _db.PendingShares
            .Include(p => p.VaultEntry)
            .Where(p => p.RecipientEmail == normalizedEmail && p.ShareCode == null && !p.IsConsumed)
            .ToListAsync();

        foreach (var pending in pendingShares)
        {
            if (pending.VaultEntry is null) continue;

            var plain = SafeDecrypt(pending.EncryptedPassword, pending.IV, BuildPendingShareKey(normalizedEmail));
            if (plain.StartsWith("[Decryption failed", StringComparison.Ordinal)) continue;

            var (shareCipher, shareIv) = _enc.Encrypt(plain, BuildShareKey(userId));
            var existingShares = await _db.SharedEntries
                .Where(s => s.VaultEntryId == pending.VaultEntryId && s.SharedWithUserId == userId)
                .OrderByDescending(s => s.SharedAt)
                .ToListAsync();

            if (existingShares.Count == 0)
            {
                _db.SharedEntries.Add(new SharedEntry
                {
                    VaultEntryId = pending.VaultEntryId,
                    SharedWithUserId = userId,
                    ReEncryptedKey = shareCipher,
                    ReEncryptedIV = shareIv,
                    SharedAt = DateTime.UtcNow
                });
            }
            else
            {
                var latest = existingShares[0];
                latest.ReEncryptedKey = shareCipher;
                latest.ReEncryptedIV = shareIv;
                latest.SharedAt = DateTime.UtcNow;
                if (existingShares.Count > 1)
                {
                    _db.SharedEntries.RemoveRange(existingShares.Skip(1));
                }
            }
        }

        if (pendingShares.Count > 0)
        {
            _db.PendingShares.RemoveRange(pendingShares);
            await _db.SaveChangesAsync();
        }
    }

    private string SafeDecrypt(string cipher, string iv, string key)
    {
        try   { return _enc.Decrypt(cipher, iv, key); }
        catch { return "[Decryption failed — wrong key?]"; }
    }

    private string SafeDecryptShared(string cipher, string iv, string userId)
    {
        try { return _enc.Decrypt(cipher, iv, BuildShareKey(userId)); } catch { }

        try { return _enc.Decrypt(cipher, iv, userId); } catch { }

        return "[Decryption failed — wrong key?]";
    }

    private string BuildShareKey(string userId) => $"{_shareSecret}:{userId}";
    private string BuildPendingShareKey(string scope) => $"{_shareSecret}:pending:{scope}";

    private static string NormalizeShareCode(string code) =>
        (code ?? "").Trim().ToUpperInvariant().Replace(" ", "");

    private async Task<string> CreateUniqueShareCodeAsync()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var bytes = new byte[10];
            RandomNumberGenerator.Fill(bytes);
            var chars = bytes.Select(b => alphabet[b % alphabet.Length]).ToArray();
            var code = $"PK-{new string(chars, 0, 4)}-{new string(chars, 4, 4)}";
            var exists = await _db.PendingShares.AnyAsync(p => p.ShareCode == code);
            if (!exists) return code;
        }

        throw new InvalidOperationException("Unable to generate a unique share code.");
    }
}
