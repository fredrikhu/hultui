using System.Text;
using Android.Security.Keystore;
using HultUI.Core.Platform;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace HultUI.Platform.Android;

/// <summary>
/// Android's secret store behind <c>CredentialStore.Default</c> (docs/design/ANDROID-PLATFORM.md
/// §2): an AES-256 key generated inside the Android Keystore, which never leaves it, encrypting
/// each secret with AES-GCM into a file in the app's private folder. Uninstalling the app removes
/// both; a backup restored to another device cannot decrypt the file, since the key stays behind.
/// </summary>
/// <remarks>
/// The file holds one line per entry: the base64 of <c>service|account</c>, a tab, the base64 IV,
/// a colon, the base64 ciphertext and tag. Written whole to a temporary file and moved over the
/// old one, so a crash mid-write leaves the old entries. Reads and writes are locked: a secret is
/// read and written from wherever an app's code runs. A secret that no longer decrypts (the key
/// was lost, say after the device's lock screen was removed) reads as absent, as the interface's
/// "the user has to enter it" asks.
/// </remarks>
internal sealed class AndroidKeystoreCredentialStore(string folder) : ICredentialStore
{
    private const string KeyStoreName = "AndroidKeyStore";
    private const string KeyAlias = "hultui.credentials";
    private const string Transformation = "AES/GCM/NoPadding";
    private const int TagBits = 128;

    private readonly string _path = Path.Combine(folder, "hultui-credentials");
    private readonly Lock _gate = new();

    public string? Read(string service, string account)
    {
        lock (_gate)
        {
            if (!Load().TryGetValue(Key(service, account), out var stored)) return null;
            try
            {
                return Decrypt(stored);
            }
            catch (Exception e) when (e is Java.Lang.Exception or FormatException)
            {
                return null;
            }
        }
    }

    public bool Write(string service, string account, string secret)
    {
        lock (_gate)
        {
            var entries = Load();
            try
            {
                entries[Key(service, account)] = Encrypt(secret);
            }
            catch (Java.Lang.Exception)
            {
                return false;
            }
            return Save(entries);
        }
    }

    public bool Delete(string service, string account)
    {
        lock (_gate)
        {
            var entries = Load();
            return !entries.Remove(Key(service, account)) || Save(entries);
        }
    }

    private static string Key(string service, string account) => service + "|" + account;

    private Dictionary<string, string> Load()
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(_path)) return entries;
        foreach (var line in File.ReadAllLines(_path))
        {
            int tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            try
            {
                entries[Encoding.UTF8.GetString(Convert.FromBase64String(line[..tab]))] = line[(tab + 1)..];
            }
            catch (FormatException)
            {
                // A damaged line is one lost entry, not a lost store.
            }
        }
        return entries;
    }

    private bool Save(Dictionary<string, string> entries)
    {
        try
        {
            string temporary = _path + ".new";
            File.WriteAllLines(temporary, entries.Select(e =>
                Convert.ToBase64String(Encoding.UTF8.GetBytes(e.Key)) + "\t" + e.Value));
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IKey TheKey()
    {
        var store = KeyStore.GetInstance(KeyStoreName)!;
        store.Load(null as KeyStore.ILoadStoreParameter);
        if (store.GetKey(KeyAlias, null) is { } existing) return existing;

        var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, KeyStoreName)!;
        generator.Init(new KeyGenParameterSpec.Builder(KeyAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)!
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)!
            .SetKeySize(256)!
            .Build());
        return generator.GenerateKey()!;
    }

    private static string Encrypt(string secret)
    {
        var cipher = Cipher.GetInstance(Transformation)!;
        cipher.Init(CipherMode.EncryptMode, TheKey());
        byte[] iv = cipher.GetIV()!;
        byte[] sealedBytes = cipher.DoFinal(Encoding.UTF8.GetBytes(secret))!;
        return Convert.ToBase64String(iv) + ":" + Convert.ToBase64String(sealedBytes);
    }

    private static string Decrypt(string stored)
    {
        int colon = stored.IndexOf(':');
        if (colon <= 0) throw new FormatException("No IV in a stored secret.");
        var cipher = Cipher.GetInstance(Transformation)!;
        cipher.Init(CipherMode.DecryptMode, TheKey(), new GCMParameterSpec(TagBits, Convert.FromBase64String(stored[..colon])));
        return Encoding.UTF8.GetString(cipher.DoFinal(Convert.FromBase64String(stored[(colon + 1)..]))!);
    }
}
