namespace HultUI.Terminal.Ssh;

/// <summary>The key a host presented, and what the known hosts make of it.</summary>
public sealed class SshHostKey
{
    internal SshHostKey(string host, int port, byte[] key, HostKeyStatus status, IReadOnlyList<string> onFile)
    {
        Host = host;
        Port = port;
        Key = key;
        Type = KnownHostsFile.KeyType(key);
        Fingerprint = KnownHostsFile.Fingerprint(key);
        Status = status;
        FingerprintsOnFile = onFile;
    }

    /// <summary>The host as connected to.</summary>
    public string Host { get; }

    /// <summary>The port connected to.</summary>
    public int Port { get; }

    /// <summary>The key's type: <c>ssh-ed25519</c>, <c>ecdsa-sha2-nistp256</c>, <c>ssh-rsa</c>.</summary>
    public string Type { get; }

    /// <summary>The key as the host sent it (SSH wire format).</summary>
    public ReadOnlyMemory<byte> Key { get; }

    /// <summary>The fingerprint as <c>ssh</c> shows it, <c>SHA256:</c> and base64: what a user
    /// compares with the one their administrator gave them.</summary>
    public string Fingerprint { get; }

    /// <summary>What the known hosts make of the key. With no known hosts file, every key is
    /// <see cref="HostKeyStatus.Unknown"/>.</summary>
    public HostKeyStatus Status { get; }

    /// <summary>When the key <see cref="HostKeyStatus.Changed"/>, the fingerprints the known hosts
    /// have for this host and key type: what a warning shows beside the new one.</summary>
    public IReadOnlyList<string> FingerprintsOnFile { get; }

    public override string ToString() => $"{Host}:{Port} {Type} {Fingerprint} ({Status})";
}

/// <summary>What to do with a host key the known hosts do not vouch for.</summary>
public enum HostKeyDecision
{
    /// <summary>Do not connect.</summary>
    Reject,

    /// <summary>Connect this time; remember nothing.</summary>
    AcceptOnce,

    /// <summary>Connect, and record the key in the known hosts: added for an unknown host,
    /// replacing the old key for a changed one.</summary>
    Trust,
}
