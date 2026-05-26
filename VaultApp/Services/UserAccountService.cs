using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VaultApp.Data;
using VaultApp.Models;

namespace VaultApp.Services;

public interface IUserAccountService
{
    Task<(bool ok, string error)> DeleteAccountAsync(
        string userId, string password, string encryptionKey);
}

public class UserAccountService : IUserAccountService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEncryptionService _enc;

    public UserAccountService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IEncryptionService enc)
    {
        _db = db;
        _userManager = userManager;
        _enc = enc;
    }

    public async Task<(bool ok, string error)> DeleteAccountAsync(
        string userId, string password, string encryptionKey)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return (false, "User not found.");

        if (string.IsNullOrEmpty(user.EncryptionKeyHash) ||
            !_enc.VerifyEncryptionKey(encryptionKey, user.EncryptionKeyHash))
            return (false, "Invalid credentials.");

        if (!await _userManager.CheckPasswordAsync(user, password))
            return (false, "Invalid credentials.");

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var sharedToUser = await _db.SharedEntries
                .Where(s => s.SharedWithUserId == userId)
                .ToListAsync();
            _db.SharedEntries.RemoveRange(sharedToUser);

            var pending = await _db.PendingShares
                .Where(p => p.UserId == userId || p.SharedWithUserId == userId)
                .ToListAsync();
            _db.PendingShares.RemoveRange(pending);

            var ownedEntries = await _db.VaultEntries
                .Where(v => v.OwnerId == userId)
                .ToListAsync();
            _db.VaultEntries.RemoveRange(ownedEntries);

            await _db.SaveChangesAsync();

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                await transaction.RollbackAsync();
                var msg = string.Join(" ", result.Errors.Select(e => e.Description));
                return (false, string.IsNullOrWhiteSpace(msg) ? "Unable to delete account." : msg);
            }

            await transaction.CommitAsync();
            return (true, "");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
