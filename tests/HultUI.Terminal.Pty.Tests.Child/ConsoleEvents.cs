using System.Runtime.InteropServices;

/// <summary>
/// The <c>events</c> mode, Windows only: reads the console's input records as a native console
/// program does and reports each, "SIZE columns rows" for a buffer resize and
/// "KEY vk scan char state" for a key press, until it reads q.
/// </summary>
internal static class ConsoleEvents
{
    public static int Run(TextWriter output)
    {
        var input = GetStdHandle(-10);
        if (!GetConsoleMode(input, out uint mode)) { output.Write("NO CONSOLE\n"); output.Flush(); return 3; }
        // Window (resize) input on, line and echo processing off: one record at a time.
        SetConsoleMode(input, (mode | 0x0008) & ~(0x0002u | 0x0004u | 0x0200u));
        output.Write("READY\n");
        output.Flush();
        var records = new INPUT_RECORD[16];
        while (ReadConsoleInputW(input, records, records.Length, out int read))
        {
            for (int i = 0; i < read; i++)
            {
                var r = records[i];
                if (r.EventType == 4)
                {
                    // What the event says, and what the buffer says at the same moment.
                    GetConsoleScreenBufferInfo(GetStdHandle(-11), out var info);
                    int w = info.Right - info.Left + 1, h = info.Bottom - info.Top + 1;
                    output.Write($"SIZE {r.Size.X} {r.Size.Y} INFO {w} {h}\n");
                }
                else if (r.EventType == 1 && r.Key.KeyDown != 0)
                {
                    output.Write($"KEY {r.Key.VirtualKeyCode} {r.Key.VirtualScanCode} {(int)r.Key.UnicodeChar} {r.Key.ControlKeyState}\n");
                    if (r.Key.UnicodeChar == 'q') { output.Flush(); return 0; }
                }
            }
            output.Flush();
        }
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEY_EVENT_RECORD
    {
        public int KeyDown;
        public ushort RepeatCount, VirtualKeyCode, VirtualScanCode;
        public ushort UnicodeChar; // not char: keeps the struct blittable, two bytes
        public uint ControlKeyState;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD { public short X, Y; }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_RECORD
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public KEY_EVENT_RECORD Key;
        [FieldOffset(4)] public COORD Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CONSOLE_SCREEN_BUFFER_INFO
    {
        public COORD Size, Cursor;
        public ushort Attributes;
        public short Left, Top, Right, Bottom;
        public COORD MaxWindow;
    }

    [DllImport("kernel32.dll")] private static extern bool GetConsoleScreenBufferInfo(nint handle, out CONSOLE_SCREEN_BUFFER_INFO info);
    [DllImport("kernel32.dll")] private static extern nint GetStdHandle(int which);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(nint handle, out uint mode);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(nint handle, uint mode);
    [DllImport("kernel32.dll")] private static extern bool ReadConsoleInputW(nint handle, [Out] INPUT_RECORD[] records, int length, out int read);
}
