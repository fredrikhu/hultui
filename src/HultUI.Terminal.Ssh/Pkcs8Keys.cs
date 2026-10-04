using System.Numerics;
using System.Security.Cryptography;
using Renci.SshNet;
using Renci.SshNet.Security;
using Renci.SshNet.Security.Cryptography;

namespace HultUI.Terminal.Ssh;

/// <summary>
/// PKCS#8 private keys (<c>BEGIN PRIVATE KEY</c>, <c>BEGIN ENCRYPTED PRIVATE KEY</c>), read by
/// .NET and handed to SSH.NET as a key.
/// </summary>
/// <remarks>
/// SSH.NET 2026.0.0 cannot read an encrypted PKCS#8 EC key as .NET writes it: having decrypted
/// it, its parser expects the public key (RFC 5915's <c>[1]</c>) where the optional curve
/// parameters (<c>[0]</c>) come, and fails with an ASN.1 error. (The same key unencrypted it
/// reads.) It also opens an encrypted PKCS#8 key with an empty passphrase rather than asking for
/// one, and the decryption's error comes out. .NET's own reader has neither problem, for EC and
/// RSA keys; other kinds are left to SSH.NET.
/// <para>A key made this way is offered with the signatures SSH.NET gives a key it read itself:
/// <c>PrivateKeyFile(Key)</c> offers an RSA key only as <c>ssh-rsa</c>, SHA-1, which OpenSSH
/// refuses since 8.8.</para>
/// </remarks>
internal static class Pkcs8Keys
{
    /// <summary>Whether the file is PKCS#8 at all.</summary>
    public static bool Is(string pem) =>
        pem.Contains("-----BEGIN PRIVATE KEY-----", StringComparison.Ordinal)
        || pem.Contains("-----BEGIN ENCRYPTED PRIVATE KEY-----", StringComparison.Ordinal);

    public static bool IsEncrypted(string pem) =>
        pem.Contains("-----BEGIN ENCRYPTED PRIVATE KEY-----", StringComparison.Ordinal);

    /// <summary>The key; null when an unencrypted file holds neither an EC nor an RSA key, which
    /// is SSH.NET's to read.</summary>
    /// <exception cref="CryptographicException">The passphrase is wrong, or the file broken. (A
    /// wrong passphrase and a key of the other algorithm look alike to .NET, so both are tried
    /// before saying so.)</exception>
    public static IPrivateKeySource? Read(string pem, string? passphrase)
    {
        Exception failure;
        try
        {
            if (Ecdsa(pem, passphrase) is { } ecdsa) return new KeySource(ecdsa);
            failure = new CryptographicException("An EC key on a curve SSH does not use.");
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException) { failure = ex; }
        try { return new KeySource(Rsa(pem, passphrase)); }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException) { }
        if (passphrase is null) return null;
        throw failure as CryptographicException ?? new CryptographicException(failure.Message, failure);
    }

    private static Key? Ecdsa(string pem, string? passphrase)
    {
        using var ec = ECDsa.Create();
        Import(ec, pem, passphrase);
        var p = ec.ExportParameters(includePrivateParameters: true);
        var curve = p.Curve.Oid.Value switch
        {
            "1.2.840.10045.3.1.7" => "nistp256",
            "1.3.132.0.34" => "nistp384",
            "1.3.132.0.35" => "nistp521",
            _ => null,
        };
        if (curve is null) return null;
        var point = new byte[1 + p.Q.X!.Length + p.Q.Y!.Length];
        point[0] = 4; // uncompressed
        p.Q.X.CopyTo(point, 1);
        p.Q.Y.CopyTo(point, 1 + p.Q.X.Length);
        return new EcdsaKey(curve, point, p.D!);
    }

    private static Key Rsa(string pem, string? passphrase)
    {
        using var rsa = RSA.Create();
        Import(rsa, pem, passphrase);
        var p = rsa.ExportParameters(includePrivateParameters: true);
        return new RsaKey(Big(p.Modulus!), Big(p.Exponent!), Big(p.D!), Big(p.P!), Big(p.Q!), Big(p.InverseQ!));

        static BigInteger Big(byte[] bytes) => new(bytes, isUnsigned: true, isBigEndian: true);
    }

    private static void Import(AsymmetricAlgorithm key, string pem, string? passphrase)
    {
        if (passphrase is null) key.ImportFromPem(pem);
        else key.ImportFromEncryptedPem(pem, passphrase);
    }

    /// <summary>A key and the signatures it is offered with: SSH.NET's own file reader's, with the
    /// SHA-2 ones first. SSH.NET offers them in order, and its own order puts <c>ssh-rsa</c> first:
    /// a round trip a current server refuses.</summary>
    private sealed class KeySource : IPrivateKeySource, IDisposable
    {
        private readonly Key _key;

        public KeySource(Key key)
        {
            _key = key;
            HostKeyAlgorithms = key is RsaKey rsa
                ?
                [
                    new KeyHostAlgorithm("rsa-sha2-512", key, new RsaDigitalSignature(rsa, HashAlgorithmName.SHA512)),
                    new KeyHostAlgorithm("rsa-sha2-256", key, new RsaDigitalSignature(rsa, HashAlgorithmName.SHA256)),
                    new KeyHostAlgorithm("ssh-rsa", key),
                ]
                : [new KeyHostAlgorithm(key.ToString(), key)];
        }

        public IReadOnlyCollection<HostAlgorithm> HostKeyAlgorithms { get; }

        public void Dispose() => (_key as IDisposable)?.Dispose();
    }
}
