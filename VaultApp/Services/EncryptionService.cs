using System.Security.Cryptography;
using System.Text;

namespace VaultApp.Services;

/// <summary>
/// All cryptographic operations for the vault.
///
/// Key derivation:  PBKDF2-SHA256, 600_000 iterations, 32-byte output
/// Encryption:      AES-256-CBC with HMAC-SHA256 authentication (encrypt-then-MAC)
/// Key hashing:     PBKDF2-SHA256 stored in DB only to VERIFY the key at login
/// </summary>
public interface IEncryptionService
{
    string HashEncryptionKey(string encryptionKey);
    bool   VerifyEncryptionKey(string encryptionKey, string storedHash);
    (string ciphertext, string iv) Encrypt(string plaintext, string encryptionKey);
    string Decrypt(string ciphertext, string iv, string encryptionKey);
    string GenerateSalt();
}

public class EncryptionService : IEncryptionService
{
    private const int Iterations = 600_000;
    private const int KeyBytes   = 32; // 256-bit
    private const int IvBytes    = 16; // 128-bit block

    // ── Key hashing (for login verification) ─────────────────────────────────
    public string HashEncryptionKey(string encryptionKey)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(encryptionKey),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeyBytes);

        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public bool VerifyEncryptionKey(string encryptionKey, string storedHash)
    {
        var parts = storedHash.Split(':');
        if (parts.Length != 2) return false;

        var salt = Convert.FromBase64String(parts[0]);
        var expectedHash = Convert.FromBase64String(parts[1]);

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(encryptionKey),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeyBytes);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    // ── AES-256-CBC + HMAC-SHA256 (Encrypt-then-MAC) ────────────────────────
    public (string ciphertext, string iv) Encrypt(string plaintext, string encryptionKey)
    {
        var (aesKey, macKey) = DeriveKeys(encryptionKey);
        var iv = RandomNumberGenerator.GetBytes(IvBytes);

        byte[] cipherBytes;
        using (var aes = Aes.Create())
        {
            aes.Key  = aesKey;
            aes.IV   = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var encryptor = aes.CreateEncryptor();
            cipherBytes = encryptor.TransformFinalBlock(
                Encoding.UTF8.GetBytes(plaintext), 0,
                Encoding.UTF8.GetBytes(plaintext).Length);
        }

        // MAC over IV + ciphertext
        var mac = ComputeMac(macKey, iv, cipherBytes);

        // Store as: ciphertext_b64:mac_b64
        var payload = $"{Convert.ToBase64String(cipherBytes)}:{Convert.ToBase64String(mac)}";
        return (payload, Convert.ToBase64String(iv));
    }

    public string Decrypt(string ciphertext, string iv, string encryptionKey)
    {
        var (aesKey, macKey) = DeriveKeys(encryptionKey);
        var ivBytes = Convert.FromBase64String(iv);

        var parts = ciphertext.Split(':');
        if (parts.Length != 2)
            throw new CryptographicException("Invalid ciphertext format.");

        var cipherBytes  = Convert.FromBase64String(parts[0]);
        var storedMac    = Convert.FromBase64String(parts[1]);
        var expectedMac  = ComputeMac(macKey, ivBytes, cipherBytes);

        if (!CryptographicOperations.FixedTimeEquals(storedMac, expectedMac))
            throw new CryptographicException("Authentication failed — data may have been tampered with.");

        using var aes = Aes.Create();
        aes.Key     = aesKey;
        aes.IV      = ivBytes;
        aes.Mode    = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    public string GenerateSalt() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static (byte[] aesKey, byte[] macKey) DeriveKeys(string encryptionKey)
    {
        // Deterministic salt derived from a fixed label so both encrypt/decrypt
        // sides produce the same key without storing the derived key.
        var label     = "VaultApp-v1"u8.ToArray();
        var aesDerived = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(encryptionKey),
            label,
            100_000,
            HashAlgorithmName.SHA256,
            64);

        return (aesDerived[..32], aesDerived[32..]);
    }

    private static byte[] ComputeMac(byte[] key, byte[] iv, byte[] cipher)
    {
        var data = new byte[iv.Length + cipher.Length];
        Buffer.BlockCopy(iv, 0, data, 0, iv.Length);
        Buffer.BlockCopy(cipher, 0, data, iv.Length, cipher.Length);
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(data);
    }
}
