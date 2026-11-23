using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.DvbI.Configuration;

/// <summary>
/// Encrypts secrets (the TVHeadend password) for storage in the plugin configuration, so they are neither
/// readable in the configuration file nor in the configuration page. AES-256-GCM with a random key kept in a
/// separate file next to the plugin configuration (it has to survive reinstalling the plugin). This does not
/// protect against someone with file access to the configuration folder or full access to the server.
/// </summary>
public sealed class SecretProtector
{
    private const string Prefix = "enc:v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private readonly string _keyFile;
    private readonly object _lock = new();
    private byte[]? _key;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretProtector"/> class.
    /// </summary>
    /// <param name="keyFile">Path of the key file.</param>
    public SecretProtector(string keyFile)
    {
        _keyFile = keyFile;
    }

    /// <summary>
    /// Gets the protector of the running plugin.
    /// </summary>
    public static SecretProtector Default => new(Plugin.Instance?.SecretKeyFile ?? Path.Combine(Path.GetTempPath(), "Jellyfin.Plugin.DvbI.key"));

    /// <summary>
    /// Returns whether a stored value is encrypted.
    /// </summary>
    /// <param name="value">Stored value.</param>
    /// <returns>True if encrypted by this class.</returns>
    public static bool IsProtected(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) ?? false;

    /// <summary>
    /// Encrypts a secret. Empty and already encrypted values are returned unchanged.
    /// </summary>
    /// <param name="plain">Plain text.</param>
    /// <returns>Encrypted value with prefix.</returns>
    public string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain) || IsProtected(plain))
        {
            return plain ?? string.Empty;
        }

        var data = Encoding.UTF8.GetBytes(plain);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(Key(), TagSize))
        {
            aes.Encrypt(nonce, data, cipher, tag);
        }

        return Prefix + Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    /// <summary>
    /// Decrypts a stored secret. Plain (legacy) values are returned unchanged.
    /// </summary>
    /// <param name="stored">Stored value.</param>
    /// <returns>Plain text, or null if the value cannot be decrypted (e.g. key file lost).</returns>
    public string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored) || !IsProtected(stored))
        {
            return stored;
        }

        try
        {
            var blob = Convert.FromBase64String(stored[Prefix.Length..]);
            if (blob.Length < NonceSize + TagSize)
            {
                return null;
            }

            var nonce = blob.AsSpan(0, NonceSize);
            var tag = blob.AsSpan(NonceSize, TagSize);
            var cipher = blob.AsSpan(NonceSize + TagSize);
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(Key(), TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }

    private byte[] Key()
    {
        lock (_lock)
        {
            if (_key is not null)
            {
                return _key;
            }

            if (File.Exists(_keyFile))
            {
                var existing = File.ReadAllBytes(_keyFile);
                if (existing.Length == KeySize)
                {
                    return _key = existing;
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_keyFile)!);
            var key = RandomNumberGenerator.GetBytes(KeySize);
            File.WriteAllBytes(_keyFile, key);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(_keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            return _key = key;
        }
    }
}
