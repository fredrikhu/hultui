using System.Runtime.InteropServices;
using System.Text;

namespace Meridian.Core;

/// <summary>
/// macOS's keyring: the login keychain, through Security.framework's generic-password calls (the
/// ones Git Credential Manager uses). An item's service is the entry name the Windows Credential
/// Manager uses (<see cref="WindowsCredentialKeyring.Target"/>) and its account is the key, so
/// Keychain Access lists <c>meridian:staging|/Users/me/payments-api|client_secret</c>.
///
/// <para>macOS ties an item to the program that made it: the first read by another build of Meridian
/// may ask the user to allow it, and the call waits for the answer. Answers are remembered for the
/// process (<see cref="KeyringMemo"/>), as on Linux, so that happens once per item, not at every
/// build of the table.</para>
/// </summary>
public sealed unsafe partial class MacKeychain : IKeyring
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int ErrSecItemNotFound = -25300;

    private readonly KeyringMemo _memo = new();

    public string? Get(string workspaceRoot, string? environment, string key)
    {
        if (!OperatingSystem.IsMacOS()) return null;
        if (_memo.TryRecall(workspaceRoot, environment, key, out var known)) return known;

        string entry = WindowsCredentialKeyring.Target(workspaceRoot, environment, key);
        byte[] service = Encoding.UTF8.GetBytes(entry), account = Encoding.UTF8.GetBytes(key);
        uint length = 0;
        nint data = 0;
        int status = SecKeychainFindGenericPassword(0, (uint)service.Length, service, (uint)account.Length, account,
            &length, &data, null);
        if (status == ErrSecItemNotFound)
        {
            _memo.Remember(workspaceRoot, environment, key, null);
            return null;
        }
        if (status != 0) return null;   // locked and not unlocked, or access refused: ask again next time
        try
        {
            string value = length == 0 ? "" : Encoding.UTF8.GetString((byte*)data, (int)length);
            _memo.Remember(workspaceRoot, environment, key, value);
            return value;
        }
        finally
        {
            SecKeychainItemFreeContent(0, data);
        }
    }

    public bool Set(string workspaceRoot, string? environment, string key, string value)
    {
        if (!OperatingSystem.IsMacOS()) return false;
        string entry = WindowsCredentialKeyring.Target(workspaceRoot, environment, key);
        byte[] service = Encoding.UTF8.GetBytes(entry), account = Encoding.UTF8.GetBytes(key);
        byte[] password = Encoding.UTF8.GetBytes(value);
        try
        {
            nint item = 0;
            int status = SecKeychainFindGenericPassword(0, (uint)service.Length, service, (uint)account.Length, account,
                null, null, &item);
            if (status == 0)
            {
                status = SecKeychainItemModifyAttributesAndData(item, 0, (uint)password.Length, password);
                CFRelease(item);
            }
            else if (status == ErrSecItemNotFound)
                status = SecKeychainAddGenericPassword(0, (uint)service.Length, service, (uint)account.Length, account,
                    (uint)password.Length, password, null);
            if (status != 0)
            {
                _memo.Forget(workspaceRoot, environment, key);
                return false;
            }
            _memo.Remember(workspaceRoot, environment, key, value);
            return true;
        }
        finally
        {
            Array.Clear(password);
        }
    }

    public bool Delete(string workspaceRoot, string? environment, string key)
    {
        if (!OperatingSystem.IsMacOS()) return false;
        string entry = WindowsCredentialKeyring.Target(workspaceRoot, environment, key);
        byte[] service = Encoding.UTF8.GetBytes(entry), account = Encoding.UTF8.GetBytes(key);
        nint item = 0;
        int status = SecKeychainFindGenericPassword(0, (uint)service.Length, service, (uint)account.Length, account,
            null, null, &item);
        if (status == ErrSecItemNotFound)
        {
            _memo.Remember(workspaceRoot, environment, key, null);
            return false;
        }
        if (status != 0)
        {
            _memo.Forget(workspaceRoot, environment, key);
            return false;
        }
        status = SecKeychainItemDelete(item);
        CFRelease(item);
        if (status != 0)
        {
            _memo.Forget(workspaceRoot, environment, key);
            return false;
        }
        _memo.Remember(workspaceRoot, environment, key, null);
        return true;
    }

    // keychainOrArray / keychain 0: the user's default search list, and the default keychain to add to.

    [LibraryImport(Security)]
    private static partial int SecKeychainFindGenericPassword(nint keychainOrArray,
        uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName,
        uint* passwordLength, nint* passwordData, nint* itemRef);

    [LibraryImport(Security)]
    private static partial int SecKeychainAddGenericPassword(nint keychain,
        uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName,
        uint passwordLength, byte[] passwordData, nint* itemRef);

    [LibraryImport(Security)]
    private static partial int SecKeychainItemModifyAttributesAndData(nint itemRef, nint attrList, uint length, byte[] data);

    [LibraryImport(Security)]
    private static partial int SecKeychainItemDelete(nint itemRef);

    [LibraryImport(Security)]
    private static partial int SecKeychainItemFreeContent(nint attrList, nint data);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint cf);
}
