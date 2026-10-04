using HultUI.Terminal;
using HultUI.Terminal.Ssh;

namespace HultUI.Samples.Terminal;

/// <summary>
/// <c>--ssh user@host[:port] [--accept-new]</c>: a shell on another machine instead of this one.
/// </summary>
/// <remarks>
/// A sample, so it asks nothing: it signs in with the user's SSH agent or a key file in
/// <c>~/.ssh</c> that has no passphrase, and trusts the hosts in <c>~/.ssh/known_hosts</c>. A new
/// host is trusted (and recorded) only with <c>--accept-new</c>, as OpenSSH's
/// <c>StrictHostKeyChecking=accept-new</c>; a changed key never is. An application would ask, with
/// <see cref="SshConnectOptions.VerifyHostKey"/> and the prompts of <see cref="SshAuthentication"/>.
/// </remarks>
internal static class SshTarget
{
    /// <summary>What connects, or null when the arguments name no host.</summary>
    /// <exception cref="ArgumentException">The target is not <c>user@host[:port]</c>.</exception>
    public static Func<ITerminalConnection>? From(string[] args)
    {
        int at = Array.IndexOf(args, "--ssh");
        if (at < 0) return null;
        var target = at + 1 < args.Length ? args[at + 1] : "";
        int sign = target.IndexOf('@');
        if (sign <= 0 || sign == target.Length - 1) throw new ArgumentException($"--ssh wants user@host[:port], not '{target}'.");
        var user = target[..sign];
        var host = target[(sign + 1)..];
        int port = 22;
        if (host.LastIndexOf(':') is int colon and > 0 && int.TryParse(host[(colon + 1)..], out var p))
            (host, port) = (host[..colon], p);
        bool acceptNew = args.Contains("--accept-new");

        var ssh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        var options = new SshConnectOptions
        {
            Host = host,
            Port = port,
            User = user,
            Authentication =
            [
                SshAuthentication.Agent(),
                SshAuthentication.KeyFile(Path.Combine(ssh, "id_ed25519")),
                SshAuthentication.KeyFile(Path.Combine(ssh, "id_ecdsa")),
                SshAuthentication.KeyFile(Path.Combine(ssh, "id_rsa")),
            ],
            VerifyHostKey = (key, _) => Task.FromResult(
                key.Status == HostKeyStatus.Unknown && acceptNew ? HostKeyDecision.Trust : HostKeyDecision.Reject),
        };
        return () =>
        {
            try { return Task.Run(() => SshConnection.ConnectAsync(options)).GetAwaiter().GetResult(); }
            catch (SshHostKeyRejectedException ex) when (ex.HostKey.Status == HostKeyStatus.Unknown)
            {
                throw new InvalidOperationException(
                    $"{host} is not a known host ({ex.HostKey.Type} {ex.HostKey.Fingerprint}); --accept-new trusts it.", ex);
            }
            catch (SshHostKeyRejectedException ex)
            {
                throw new InvalidOperationException(
                    $"{host}'s key is {ex.HostKey.Status}: {ex.HostKey.Fingerprint}. Not connecting.", ex);
            }
        };
    }
}
