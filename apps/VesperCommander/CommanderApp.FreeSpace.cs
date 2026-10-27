namespace VesperCommander;

public partial class CommanderApp
{
    // ── Free space (the footers) ─────────────────────────────────────────
    //
    // DriveInfo.AvailableFreeSpace asks the volume: a round trip to the disk, or across the
    // network for a share. The footer asked on every build of itself, on the UI thread: once for
    // a mark or a type-to-find key, twice for a folder change and twice for each transfer repaint.
    // It now asks when the answer can have moved: the pane shows another folder, or the folder's
    // listing is read again — which every file operation asks for when it completes (it bumps the
    // panes' generations: a copy, a move, a delete, a pack, an extract, a drop), and so does a
    // refresh (Ctrl+R). A volume filled by another program shows its new free space at the next
    // of those, not at the next key.

    /// <summary>Where a footer's free-space label comes from: the volume, or a test's.</summary>
    internal Func<string, string> FreeLabelSource { get; set; } = FileSystem.FreeLabel;

    private readonly (string Dir, int Gen, string Label)?[] _freeLabels = new (string, int, string)?[2];

    /// <summary>The free space a pane's footer shows, asked of the volume once for each folder and
    /// listing generation the pane shows.</summary>
    private string FreeLabelOf(char side)
    {
        int i = Si(side);
        string dir = Dir(side);
        int gen = Gen(side);
        if (_freeLabels[i] is { } kept && kept.Gen == gen && kept.Dir == dir) return kept.Label;
        string label = FreeLabelSource(dir);
        _freeLabels[i] = (dir, gen, label);
        return label;
    }
}
