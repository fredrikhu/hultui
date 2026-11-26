using System.Text;
using Log = Android.Util.Log;

namespace Aperture.Android;

/// <summary>
/// stderr into logcat, a line at a time, under the tag "HultUI": what the diagnostics HultUI turns
/// on with an environment variable write there (HULTUI_MPV_LOG, mpv's own log and its https
/// requests). On Android nothing reads the process's stderr, so without this they said nothing.
/// Debug builds only, for a run started with <c>-e env</c>.
/// </summary>
internal sealed class LogcatWriter : TextWriter
{
    private readonly StringBuilder _line = new();
    private readonly Lock _lock = new();

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        lock (_lock)
        {
            if (value == '\n') Flush();
            else if (value != '\r') _line.Append(value);
        }
    }

    public override void Write(string? value)
    {
        if (value is null) return;
        lock (_lock)
            foreach (char c in value) Write(c);
    }

    public override void WriteLine(string? value)
    {
        lock (_lock)
        {
            Write(value);
            Flush();
        }
    }

    public override void Flush()
    {
        lock (_lock)
        {
            if (_line.Length == 0) return;
            Log.Info("HultUI", _line.ToString());
            _line.Clear();
        }
    }
}
