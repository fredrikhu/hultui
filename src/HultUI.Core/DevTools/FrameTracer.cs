using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace HultUI.Core.DevTools;

/// <summary>
/// Records per-component layout and render timing in Chrome Trace Event Format.
/// Load the resulting .json file in https://ui.perfetto.dev or chrome://tracing.
/// When not recording, <see cref="Begin"/> returns a zero-allocation no-op scope.
/// </summary>
public sealed class FrameTracer
{
    private readonly List<TraceEvent> _events = new();
    private long _recordingStart;
    private int _frameCounter;

    public bool IsRecording { get; private set; }

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

    public Scope Begin(string category, string name, string? detail = null)
    {
        if (!IsRecording) return default;
        return new Scope(this, category, name, detail, Stopwatch.GetTimestamp());
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

    private void Record(string category, string name, string? detail, long startTs, long endTs)
    {
        long tsMicros = ToMicros(startTs - _recordingStart);
        long durMicros = ToMicros(endTs - startTs);
        var evt = new TraceEvent(category, name, detail, tsMicros, durMicros, _frameCounter);
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
        for (int i = 0; i < _events.Count; i++)
        {
            if (i > 0) sb.Append(',');
            AppendEvent(sb, _events[i]);
        }
        sb.Append(']');
        File.WriteAllText(path, sb.ToString());
    }

    private static void AppendEvent(StringBuilder sb, TraceEvent e)
    {
        sb.Append("{\"ph\":\"X\",\"pid\":1,\"tid\":1,\"cat\":\"");
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
        string Category, string Name, string? Detail, long TsMicros, long DurMicros, int Frame);

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
