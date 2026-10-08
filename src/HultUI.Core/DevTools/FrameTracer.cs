using System.Diagnostics;
using System.Globalization;
using System.Text;
using HultUI.Core.Elements;

namespace HultUI.Core.DevTools;

/// <summary>
/// Records per-component layout and render timing in Chrome Trace Event Format.
/// Load the resulting .json file in https://ui.perfetto.dev or chrome://tracing.
/// When not recording, <see cref="Begin"/> returns a zero-allocation no-op scope.
/// </summary>
public sealed class FrameTracer
{
    private readonly List<TraceEvent> _events = new();
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    private long _recordingStart;
    private int _frameCounter;

    public bool IsRecording { get; private set; }

    /// <summary>
    /// Where the F11 hotkey saves a trace, for callers that have no path of their own.
    ///
    /// <para>Asked of <see cref="Path.GetTempPath"/> rather than spelled <c>/tmp</c>: that
    /// literal is drive-relative on Windows, so it resolved to <c>tmp\</c> at the root of
    /// whichever drive the app happened to be running from — a directory that exists on
    /// nobody's machine by default. Pressing F11 twice then threw out of the input handler
    /// instead of writing a trace. GetTempPath reads TMP then TEMP on Windows and TMPDIR on
    /// Unix, where it still falls back to <c>/tmp</c> — so the path this replaces is exactly
    /// what it keeps returning there.</para>
    /// </summary>
    public static string DefaultPath { get; } =
        Path.Combine(Path.GetTempPath(), "hultui-trace.json");

    public void StartRecording()
    {
        _events.Clear();
        _frameCounter = 0;
        _recordingStart = Stopwatch.GetTimestamp();
        IsRecording = true;
    }

    public void StopRecording(string path)
    {
        IsRecording = false;
        WriteJson(path);
        _events.Clear();
    }

    /// <summary>
    /// Begins a timed scope. Nothing is recorded unless <see cref="IsRecording"/>, but the
    /// arguments are built either way: pass strings that already exist, such as literals. A
    /// name computed per call costs on every call, recording or not.
    /// </summary>
    public Scope Begin(string category, string name, string? detail = null)
    {
        if (!IsRecording) return default;
        return new Scope(this, category, name, detail, Stopwatch.GetTimestamp());
    }

    /// <summary>
    /// Begins a scope for one element in the layout or render pass: named after the
    /// element's type, with its <see cref="Element.Key"/> or <see cref="Element.Name"/> as
    /// the detail. The name is looked up only while recording. Those passes open a scope
    /// for every element they visit, every frame, and on NativeAOT <c>Type.Name</c> builds a
    /// new string on every call. Built up front, the names were 683 of the 686 KB a frame of
    /// a 2,000-row resize allocated on NativeAOT.
    /// </summary>
    internal Scope BeginElement(string category, Element element)
    {
        if (!IsRecording) return default;
        return new Scope(this, category, ElementTypeName(element), element.Key ?? element.Name,
            Stopwatch.GetTimestamp());
    }

    /// <summary>
    /// How many names <see cref="BeginElement"/> has looked up: one per element scope while
    /// recording, none otherwise. Tests read it to see that no pass builds a name it will not
    /// record.
    /// </summary>
    internal int ElementNameLookups { get; private set; }

    private string ElementTypeName(Element element)
    {
        ElementNameLookups++;
        return element.GetType().Name;
    }

    /// <summary>
    /// Async-safe alternative to <see cref="Begin"/>. Returns an <see cref="IDisposable"/>
    /// that can survive <c>await</c> boundaries. Costs one small allocation per scope when
    /// recording; returns a cached no-op when not recording.
    /// </summary>
    public IDisposable BeginAsync(string category, string name, string? detail = null)
    {
        if (!IsRecording) return AsyncScope.NoOp;
        return new AsyncScope(this, category, name, detail, Stopwatch.GetTimestamp());
    }

    public void MarkFrame()
    {
        if (!IsRecording) return;
        _frameCounter++;
    }

    /// <summary>
    /// Records a zero-duration metadata event (category "Meta") carrying
    /// <paramref name="detail"/> in its args. Used to stamp environment facts —
    /// e.g. the achieved GL configuration — into the trace so a saved file is
    /// self-describing when compared across machines or operating systems.
    /// </summary>
    public void Annotate(string name, string detail)
    {
        if (!IsRecording) return;
        long ts = Stopwatch.GetTimestamp();
        Record("Meta", name, detail, ts, ts);
    }

    private void Record(string category, string name, string? detail, long startTs, long endTs)
    {
        long tsMicros = ToMicros(startTs - _recordingStart);
        long durMicros = ToMicros(endTs - startTs);
        int tid = Environment.CurrentManagedThreadId;
        var evt = new TraceEvent(category, name, detail, tsMicros, durMicros, _frameCounter, tid);
        lock (_events)
            _events.Add(evt);
    }

    private static long ToMicros(long stopwatchTicks)
    {
        return stopwatchTicks * 1_000_000L / Stopwatch.Frequency;
    }

    private void WriteJson(string path)
    {
        var sb = new StringBuilder(_events.Count * 96);
        sb.Append('[');
        AppendThreadMetadata(sb);
        for (int i = 0; i < _events.Count; i++)
        {
            sb.Append(',');
            AppendEvent(sb, _events[i]);
        }
        sb.Append(']');
        // HULTUI_TRACE can name a path several directories deep that nothing has created
        // yet; a trace lost at the last step because of that wastes the whole run.
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } dir)
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, sb.ToString());
    }

    private void AppendThreadMetadata(StringBuilder sb)
    {
        // Emit a thread_name metadata event per unique tid seen, so Perfetto
        // labels the tracks as "UI", "Worker 7", etc. instead of raw ids.
        var seen = new HashSet<int>();
        bool first = true;
        foreach (var e in _events)
        {
            if (!seen.Add(e.Tid)) continue;
            if (!first) sb.Append(',');
            first = false;
            string label = e.Tid == _uiThreadId ? "UI" : $"Worker {e.Tid}";
            sb.Append("{\"name\":\"thread_name\",\"ph\":\"M\",\"pid\":1,\"tid\":")
                .Append(e.Tid)
                .Append(",\"args\":{\"name\":\"")
                .Append(label)
                .Append("\"}}");
        }
    }

    private static void AppendEvent(StringBuilder sb, TraceEvent e)
    {
        sb.Append("{\"ph\":\"X\",\"pid\":1,\"tid\":").Append(e.Tid).Append(",\"cat\":\"");
        AppendEscaped(sb, e.Category);
        sb.Append("\",\"name\":\"");
        AppendEscaped(sb, e.Name);
        sb.Append("\",\"ts\":").Append(e.TsMicros.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"dur\":").Append(e.DurMicros.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"args\":{\"frame\":").Append(e.Frame);
        if (e.Detail is not null)
        {
            sb.Append(",\"detail\":\"");
            AppendEscaped(sb, e.Detail);
            sb.Append('"');
        }
        sb.Append("}}");
    }

    private static void AppendEscaped(StringBuilder sb, string s)
    {
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append($"\\u{(int)c:x4}");
                    else sb.Append(c);
                    break;
            }
        }
    }

    private readonly record struct TraceEvent(
        string Category, string Name, string? Detail, long TsMicros, long DurMicros, int Frame, int Tid);

    private sealed class AsyncScope : IDisposable
    {
        internal static readonly AsyncScope NoOp = new(null, "", "", null, 0);

        private readonly FrameTracer? _tracer;
        private readonly string _category;
        private readonly string _name;
        private readonly string? _detail;
        private readonly long _startTs;

        internal AsyncScope(FrameTracer? tracer, string category, string name, string? detail, long startTs)
        {
            _tracer = tracer;
            _category = category;
            _name = name;
            _detail = detail;
            _startTs = startTs;
        }

        public void Dispose()
        {
            if (_tracer is null) return;
            _tracer.Record(_category, _name, _detail, _startTs, Stopwatch.GetTimestamp());
        }
    }

    public readonly ref struct Scope
    {
        private readonly FrameTracer? _tracer;
        private readonly string _category;
        private readonly string _name;
        private readonly string? _detail;
        private readonly long _startTs;

        internal Scope(FrameTracer tracer, string category, string name, string? detail, long startTs)
        {
            _tracer = tracer;
            _category = category;
            _name = name;
            _detail = detail;
            _startTs = startTs;
        }

        public void Dispose()
        {
            if (_tracer is null) return;
            _tracer.Record(_category, _name, _detail, _startTs, Stopwatch.GetTimestamp());
        }
    }
}
