using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    private bool                          _pendingShareSchemaEnsured;

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
        await EnsurePendingShareSchemaAsync();

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
                p => p.VaultEntryId == entryId && p.RecipientEmail == normalizedEmail);

            if (existingPending is null)
            {
                _db.PendingShares.Add(new PendingShare
                {
                    VaultEntryId = entryId,
                    RecipientEmail = normalizedEmail,
                    EncryptedPassword = pendingCipher,
                    IV = pendingIv,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existingPending.EncryptedPassword = pendingCipher;
                existingPending.IV = pendingIv;
                existingPending.CreatedAt = DateTime.UtcNow;
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
        await EnsurePendingShareSchemaAsync();

        var normalizedEmail = (email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail)) return;

        var pendingShares = await _db.PendingShares
            .Include(p => p.VaultEntry)
            .Where(p => p.RecipientEmail == normalizedEmail)
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
        // v2: server-scoped share key + recipient id
        try { return _enc.Decrypt(cipher, iv, BuildShareKey(userId)); } catch { }

        // backward-compat for earlier attempt where recipient id only was used
        try { return _enc.Decrypt(cipher, iv, userId); } catch { }

        return "[Decryption failed — wrong key?]";
    }

    private string BuildShareKey(string userId) => $"{_shareSecret}:{userId}";
    private string BuildPendingShareKey(string email) => $"{_shareSecret}:pending:{email}";

    private async Task EnsurePendingShareSchemaAsync()
    {
        if (_pendingShareSchemaEnsured) return;

        const string sql = """
IF OBJECT_ID('dbo.PendingShares', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PendingShares](
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [VaultEntryId] INT NOT NULL,
        [RecipientEmail] NVARCHAR(256) NOT NULL,
        [EncryptedPassword] NVARCHAR(MAX) NOT NULL,
        [IV] NVARCHAR(MAX) NOT NULL,
        [CreatedAt] DATETIME2 NOT NULL,
        CONSTRAINT [FK_PendingShares_VaultEntries_VaultEntryId]
            FOREIGN KEY([VaultEntryId]) REFERENCES [dbo].[VaultEntries]([Id]) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX [IX_PendingShares_VaultEntryId_RecipientEmail]
        ON [dbo].[PendingShares] ([VaultEntryId], [RecipientEmail]);
END
""";

        await _db.Database.ExecuteSqlRawAsync(sql);
        _pendingShareSchemaEnsured = true;
    }
}
