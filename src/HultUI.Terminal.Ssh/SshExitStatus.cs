using System.Reflection;
using System.Runtime.CompilerServices;
using Renci.SshNet;
using Renci.SshNet.Messages.Connection;

namespace HultUI.Terminal.Ssh;

/// <summary>
/// The remote program's exit status, which SSH.NET receives for a shell and does not hand on:
/// <see cref="ShellStream"/> keeps its channel private, and the <c>exit-status</c> request
/// arrives as an internal type. <c>SshCommand</c> reads it, but cannot ask for a terminal.
/// </summary>
/// <remarks>
/// Reached with <see cref="UnsafeAccessorAttribute"/> and <see cref="UnsafeAccessorTypeAttribute"/>,
/// which name the internals at compile time, so NativeAOT keeps them and no reflection is needed
/// beyond making the handler. Written against SSH.NET 2026.0.0, which the package pins exactly; a
/// version that moves any of it makes <see cref="TryWatch"/> return false, the exit is reported
/// without a code, and the package's exit-code test fails.
/// </remarks>
internal sealed class SshExitStatus
{
    private const string ChannelRequestEventArgs = "Renci.SshNet.Common.ChannelRequestEventArgs, Renci.SshNet";

    private const long None = long.MinValue;
    private long _code = None;

    /// <summary>The exit status, or 128 plus the signal's number when a signal ended it, as a
    /// shell reports it; null when neither arrived.</summary>
    public int? Code => Interlocked.Read(ref _code) is var code and not None ? (int)code : null;

    /// <summary>Starts listening to the shell's channel for its end. False when this SSH.NET has
    /// moved the parts it reads.</summary>
    public bool TryWatch(ShellStream shell)
    {
        try
        {
            // A field accessor cannot return a field of a type it cannot name; reflection on a
            // constant name of a known type is something the trimmer follows.
            var channel = typeof(ShellStream).GetField("_channel", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(shell);
            if (channel is null) return false;
            var handlerType = Type.GetType($"System.EventHandler`1[[{ChannelRequestEventArgs}]]", throwOnError: true)!;
            var handler = Delegate.CreateDelegate(handlerType, this,
                typeof(SshExitStatus).GetMethod(nameof(OnRequest), BindingFlags.NonPublic | BindingFlags.Instance)!);
            AddRequestReceived(channel, handler);
            return true;
        }
        catch (Exception ex) when (ex is MissingMemberException or TypeLoadException or ArgumentException
            or InvalidCastException or FileNotFoundException or NotSupportedException)
        {
            return false;
        }
    }

    private void OnRequest(object? sender, EventArgs e)
    {
        try
        {
            var info = InfoOf(e);
            int? code = info.RequestName switch
            {
                "exit-status" => (int)ExitStatusOf(info),
                "exit-signal" => SignalNumber(SignalNameOf(info)) is int n ? 128 + n : null,
                _ => null,
            };
            if (code is int value) Interlocked.Exchange(ref _code, value);
        }
        catch (Exception ex) when (ex is MissingMemberException or InvalidCastException or NotSupportedException) { }
    }

    /// <summary>POSIX's signal numbers, which are the same on every system for these; the
    /// protocol sends the name without <c>SIG</c>.</summary>
    internal static int? SignalNumber(string? name) => name switch
    {
        "HUP" => 1, "INT" => 2, "QUIT" => 3, "ILL" => 4, "ABRT" => 6, "FPE" => 8, "KILL" => 9,
        "SEGV" => 11, "PIPE" => 13, "ALRM" => 14, "TERM" => 15,
        _ => null,
    };

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "add_RequestReceived")]
    private static extern void AddRequestReceived(
        [UnsafeAccessorType("Renci.SshNet.Channels.Channel, Renci.SshNet")] object channel,
        [UnsafeAccessorType($"System.EventHandler`1[[{ChannelRequestEventArgs}]], System.Private.CoreLib")] object handler);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Info")]
    private static extern RequestInfo InfoOf([UnsafeAccessorType(ChannelRequestEventArgs)] object args);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_ExitStatus")]
    private static extern uint ExitStatusOf(
        [UnsafeAccessorType("Renci.SshNet.Messages.Connection.ExitStatusRequestInfo, Renci.SshNet")] object info);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_SignalName")]
    private static extern string? SignalNameOf(
        [UnsafeAccessorType("Renci.SshNet.Messages.Connection.ExitSignalRequestInfo, Renci.SshNet")] object info);
}
