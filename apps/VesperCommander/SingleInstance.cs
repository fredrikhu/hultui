using System.IO.Pipes;

namespace VesperCommander;

/// <summary>Single-instance IPC: the first (owning) instance runs a named-pipe server; a
/// second launch hands its folder to that server and exits, so the running window opens the
/// folder as a new tab instead of a second window appearing.</summary>
public static class SingleInstance
{
    private const string PipeName = "VesperCommander.OpenTab";

    /// <summary>Second-launch side: connect to the running instance and send a folder path.
    /// Returns false (caller can fall back to opening normally) if nothing is listening.</summary>
    public static bool TrySignal(string path)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(500);
            using var w = new StreamWriter(client);
            w.Write(path);
            w.Flush();
            return true;
        }
        catch { return false; }
    }

    /// <summary>First-instance side: accept connections on a background thread and hand each
    /// received path to <paramref name="onPath"/> (the caller marshals to the UI thread).</summary>
    public static void StartServer(Action<string> onPath, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(ct);
                    using var reader = new StreamReader(server);
                    var line = (await reader.ReadToEndAsync(ct)).Trim();
                    if (line.Length > 0) onPath(line);
                }
                catch (OperationCanceledException) { break; }
                catch { /* a malformed / dropped connection — keep listening */ }
            }
        }, ct);
    }
}
