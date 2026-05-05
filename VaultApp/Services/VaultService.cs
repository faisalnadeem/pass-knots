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
    Task<(bool ok, string error)>   ShareAsync(int entryId, string ownerId, string ownerKey, string recipientEmail);
    Task<List<DecryptedVaultEntry>> GetSharedWithMeAsync(string userId, string encKey);
}

public class VaultService : IVaultService
{
    private readonly ApplicationDbContext _db;
    private readonly IEncryptionService   _enc;

    public VaultService(ApplicationDbContext db, IEncryptionService enc)
    {
        _db  = db;
        _enc = enc;
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
            Notes             = vm.Notes,
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
        entry.Notes             = vm.Notes;
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

        // Check not already shared
        bool alreadyShared = await _db.SharedEntries.AnyAsync(
            s => s.VaultEntryId == entryId && s.SharedWithUserId == recipient.Id);
        if (alreadyShared) return (false, "Already shared with this user.");

        // Decrypt the plaintext password with owner's key, then re-encrypt with recipient's
        // deterministic derived key (recipient must supply their key to decrypt later).
        // We store ciphertext encrypted with RECIPIENT's key so only they can read it.
        string plainPwd = _enc.Decrypt(entry.EncryptedPassword, entry.IV, ownerKey);
        var (recipCipher, recipIv) = _enc.Encrypt(plainPwd, recipient.EncryptionKeyHash ?? "");

        // We can't encrypt with the recipient's actual key (we don't know it).
        // Instead store a copy of the ciphertext re-encrypted under a shared-entry-specific
        // approach: encrypt with the OWNER's key and record it — recipient downloads and
        // the owner's key version is what's stored. The recipient must request the owner
        // to reveal, OR we use a simpler UX: store plaintext re-encrypted with recipient's
        // KEY HASH as a stand-in. 
        //
        // Proper zero-knowledge sharing requires the recipient's public key.
        // For this app we use a pragmatic approach: the shared copy is encrypted with the
        // owner's key; when the recipient views it they see the site/username but must ask
        // the owner for the actual password reveal via the owner decrypting for them.
        // OR — simpler and honest — we store it encrypted with the owner's key and display
        // a note that the entry was shared (recipient can see metadata, not password, unless
        // the owner re-encrypts for them with a session token).
        //
        // IMPLEMENTATION CHOICE: Store the password re-encrypted specifically so the
        // *owner*'s session is used to produce a shareable copy. We'll flag this clearly in UI.
        var (shareCipher, shareIv) = _enc.Encrypt(plainPwd, ownerKey + ":shared:" + recipient.Id);

        var share = new SharedEntry
        {
            VaultEntryId      = entryId,
            SharedWithUserId  = recipient.Id,
            ReEncryptedKey    = shareCipher,
            ReEncryptedIV     = shareIv,
            SharedAt          = DateTime.UtcNow
        };
        _db.SharedEntries.Add(share);
        await _db.SaveChangesAsync();
        return (true, "");
    }

    public async Task<List<DecryptedVaultEntry>> GetSharedWithMeAsync(string userId, string encKey)
    {
        // For shared entries the password cannot be decrypted without the owner's key.
        // We return the entry metadata and mark PlainPassword as "[Protected — contact owner]"
        var shared = await _db.SharedEntries
            .Include(s => s.VaultEntry)
            .ThenInclude(v => v!.Owner)
            .Where(s => s.SharedWithUserId == userId)
            .OrderByDescending(s => s.SharedAt)
            .ToListAsync();

        return shared.Select(s => new DecryptedVaultEntry
        {
            Entry         = s.VaultEntry!,
            PlainPassword = "[Contact entry owner to reveal password]",
            IsShared      = true
        }).ToList();
    }

    private string SafeDecrypt(string cipher, string iv, string key)
    {
        try   { return _enc.Decrypt(cipher, iv, key); }
        catch { return "[Decryption failed — wrong key?]"; }
    }
}
