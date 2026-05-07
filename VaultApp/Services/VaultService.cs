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
    Task<(bool ok, string error)>   ShareAsync(int entryId, string ownerId, string ownerKey, string recipientEmail);
    Task<List<DecryptedVaultEntry>> GetSharedWithMeAsync(string userId, string encKey);
    Task<List<ShareRecipientViewModel>> GetSharedRecipientsAsync(int entryId, string ownerId);
    Task<bool>                      UnshareAsync(int sharedEntryId, string ownerId);
}

public class VaultService : IVaultService
{
    private readonly ApplicationDbContext _db;
    private readonly IEncryptionService   _enc;
    private readonly string               _shareSecret;

    public VaultService(ApplicationDbContext db, IEncryptionService enc, IConfiguration config)
    {
        _db  = db;
        _enc = enc;
        _shareSecret = config["Security:ShareKey"] ?? "VaultApp-Share-Key-Change-In-Production";
    }

    public async Task<List<DecryptedVaultEntry>> GetEntriesAsync(string userId, string encKey)
    {
        var entries = await _db.VaultEntries
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

    public async Task<(bool ok, string error)> ShareAsync(
        int entryId, string ownerId, string ownerKey, string recipientEmail)
    {
        var entry = await _db.VaultEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.OwnerId == ownerId);
        if (entry is null) return (false, "Entry not found.");

        var recipient = await _db.Users.FirstOrDefaultAsync(u => u.Email == recipientEmail);
        if (recipient is null) return (false, "No user found with that email.");
        if (recipient.Id == ownerId) return (false, "You cannot share with yourself.");

        // Decrypt with owner's key, then create a share copy scoped to recipient identity.
        string plainPwd = _enc.Decrypt(entry.EncryptedPassword, entry.IV, ownerKey);
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
        return (true, "");
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
}
