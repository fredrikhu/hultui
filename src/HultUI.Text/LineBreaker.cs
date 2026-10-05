namespace HultUI.Text;

/// <summary>
/// Finds line break opportunities in text using ICU's UAX #14 line break rules.
/// The library loading and versioned-symbol resolution live in
/// <see cref="IcuLibrary"/>, shared with <see cref="BidiAnalyzer"/>.
/// </summary>
internal static class LineBreaker
{
    private static ubrk_open_delegate? _open;
    private static ubrk_setText_delegate? _setText;
    private static ubrk_next_delegate? _next;
    private static ubrk_close_delegate? _close;

    // One line-break iterator per thread, pointed at each text in turn with ubrk_setText.
    // Opening one builds ICU's rule-based iterator: ~6 µs, measured — more than finding
    // every break in a 90-character label, and it was paid on every call, every width of
    // a resize drag. ICU iterators are not thread-safe, hence one per thread; the holder's
    // finalizer closes it once its thread is gone.
    [ThreadStatic] private static Iterator? t_iterator;

    public static unsafe List<int> FindBreakOpportunities(string text)
    {
        var breaks = new List<int>();
        if (string.IsNullOrEmpty(text)) return breaks;

        var iterator = t_iterator ??= Iterator.Open();
        if (iterator is null) return breaks;

        fixed (char* pText = text)
        {
            int status = 0;
            // Resets the iterator to the start of the new text. ICU does not read the text
            // again until the next call here sets another one, so the pointer outliving
            // this fixed block is never dereferenced.
            _setText!(iterator.Handle, pText, text.Length, ref status);
            if (status > 0) return breaks;

            int pos;
            while ((pos = _next!(iterator.Handle)) != -1) // -1 = UBRK_DONE
            {
                breaks.Add(pos);
            }
        }
        return breaks;
    }

    private sealed class Iterator
    {
        public readonly IntPtr Handle;

        private Iterator(IntPtr handle) => Handle = handle;

        // Null when ICU cannot open one; the caller then finds no breaks, as before, and
        // tries again next time.
        public static unsafe Iterator? Open()
        {
            _open ??= IcuLibrary.GetDelegate<ubrk_open_delegate>("ubrk_open");
            _setText ??= IcuLibrary.GetDelegate<ubrk_setText_delegate>("ubrk_setText");
            _next ??= IcuLibrary.GetDelegate<ubrk_next_delegate>("ubrk_next");
            _close ??= IcuLibrary.GetDelegate<ubrk_close_delegate>("ubrk_close");

            int status = 0;
            // 2 = UBRK_LINE; no text yet, ubrk_setText supplies it.
            IntPtr handle = _open(2, "", null, 0, ref status);
            if (status > 0 || handle == IntPtr.Zero) return null;
            return new Iterator(handle);
        }

        ~Iterator() => _close!(Handle);
    }

    private unsafe delegate IntPtr ubrk_open_delegate(int type, string locale, char* text, int textLen, ref int status);
    private unsafe delegate void ubrk_setText_delegate(IntPtr iter, char* text, int textLen, ref int status);
    private delegate int ubrk_next_delegate(IntPtr iter);
    private delegate void ubrk_close_delegate(IntPtr iter);
}
