using System.IO.Pipes;

namespace Reel;

/// <summary>
/// Single-instance IPC: the running window owns a named pipe, and a second launch hands
/// its file over and exits. Without it, "set Reel as the default player" and selecting a
/// season's worth of episodes opens a window per file.
///
/// <para>Vesper's arrangement, narrowed to one path per connection — the shell launches one
/// process per file, so each one has exactly one thing to say.</para>
/// </summary>
public static class SingleInstance
{
    private const string PipeName = "Reel.OpenFile";

    /// <summary>Second-launch side: hand the file to the running window. False when nothing
    /// is listening, which leaves the caller free to open its own window.</summary>
    public static bool TrySignal(string path)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(500);
            using var writer = new StreamWriter(client);
            writer.Write(path);
            writer.Flush();
            return true;
        }
        catch { return false; }
    }

    /// <summary>First-instance side: accept connections on a background thread and hand each
    /// path to <paramref name="onPath"/>, which marshals to the UI thread itself.</summary>
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
                catch { /* a dropped or malformed connection — keep listening */ }
            }
        }, ct);
    }
}
