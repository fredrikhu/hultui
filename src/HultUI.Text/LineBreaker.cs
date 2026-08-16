namespace HultUI.Text;

/// <summary>
/// Finds line break opportunities in text using ICU's UAX #14 line break rules.
/// The library loading and versioned-symbol resolution live in
/// <see cref="IcuLibrary"/>, shared with <see cref="BidiAnalyzer"/>.
/// </summary>
internal static class LineBreaker
{
    public static unsafe List<int> FindBreakOpportunities(string text)
    {
        var breaks = new List<int>();
        if (string.IsNullOrEmpty(text)) return breaks;

        fixed (char* pText = text)
        {
            int status = 0;

            var open = IcuLibrary.GetDelegate<ubrk_open_delegate>("ubrk_open");
            var next = IcuLibrary.GetDelegate<ubrk_next_delegate>("ubrk_next");
            var close = IcuLibrary.GetDelegate<ubrk_close_delegate>("ubrk_close");

            // 2 = UBRK_LINE
            IntPtr iterator = open(2, "", pText, text.Length, ref status);

            if (status > 0 || iterator == IntPtr.Zero) return breaks;

            try
            {
                int pos;
                while ((pos = next(iterator)) != -1) // -1 = UBRK_DONE
                {
                    breaks.Add(pos);
                }
            }
            finally
            {
                close(iterator);
            }
        }
        return breaks;
    }

    private unsafe delegate IntPtr ubrk_open_delegate(int type, string locale, char* text, int textLen, ref int status);
    private delegate int ubrk_next_delegate(IntPtr iter);
    private delegate void ubrk_close_delegate(IntPtr iter);
}
