using HultUI.Core.Platform;

namespace HultUI.WinForms;

/// <summary>
/// Translation between the framework's drag vocabulary and WinForms'.
/// </summary>
/// <remarks>
/// The two enums happen to share the same bit values today, and this maps them member
/// by member anyway. A cast would work until one of them gained a member or renumbered,
/// and the failure would be a silent wrong effect — a Move reported as a Copy leaves the
/// source holding a file it promised to delete.
/// </remarks>
internal static class DragDropInterop
{
    internal static DragDropEffects ToWinForms(DragEffect effect)
    {
        var result = DragDropEffects.None;
        if (effect.HasFlag(DragEffect.Copy)) result |= DragDropEffects.Copy;
        if (effect.HasFlag(DragEffect.Move)) result |= DragDropEffects.Move;
        if (effect.HasFlag(DragEffect.Link)) result |= DragDropEffects.Link;
        return result;
    }

    internal static DragEffect ToFramework(DragDropEffects effects)
    {
        var result = DragEffect.None;
        if (effects.HasFlag(DragDropEffects.Copy)) result |= DragEffect.Copy;
        if (effects.HasFlag(DragDropEffects.Move)) result |= DragEffect.Move;
        if (effects.HasFlag(DragDropEffects.Link)) result |= DragEffect.Link;
        return result;
    }

    /// <summary>
    /// Picks the effect to report for an incoming drag, from what the source allows and
    /// what the user is holding down.
    /// </summary>
    /// <remarks>
    /// The framework decides for itself whether a drop is accepted — a target that does
    /// not want it simply leaves <c>DragOverEvent.Accepted</c> false — but Windows will
    /// not deliver the drop at all unless the control claims an effect first. So this
    /// claims one optimistically, which is also what the SDL backend effectively does:
    /// SDL accepts every drop the OS offers and lets the tree decide.
    ///
    /// <para>Shift means move and Ctrl means copy, the Windows convention every other
    /// application follows.</para>
    /// </remarks>
    internal static DragDropEffects PreferredEffect(DragDropEffects allowed, Keys modifiers)
    {
        if ((modifiers & Keys.Shift) != 0 && allowed.HasFlag(DragDropEffects.Move))
            return DragDropEffects.Move;
        if ((modifiers & Keys.Control) != 0 && allowed.HasFlag(DragDropEffects.Copy))
            return DragDropEffects.Copy;

        if (allowed.HasFlag(DragDropEffects.Copy)) return DragDropEffects.Copy;
        if (allowed.HasFlag(DragDropEffects.Move)) return DragDropEffects.Move;
        if (allowed.HasFlag(DragDropEffects.Link)) return DragDropEffects.Link;
        return DragDropEffects.None;
    }

    /// <summary>The dragged file paths, or an empty list when the payload carries none.</summary>
    internal static IReadOnlyList<string> Files(IDataObject? data)
    {
        if (data?.GetDataPresent(DataFormats.FileDrop) != true) return [];
        return data.GetData(DataFormats.FileDrop) as string[] ?? [];
    }

    /// <summary>The dragged text, or null when the payload carries none.</summary>
    internal static string? Text(IDataObject? data)
    {
        if (data is null) return null;
        if (data.GetDataPresent(DataFormats.UnicodeText)) return data.GetData(DataFormats.UnicodeText) as string;
        if (data.GetDataPresent(DataFormats.Text)) return data.GetData(DataFormats.Text) as string;
        return null;
    }

    /// <summary>The framework's modifier flags out of the raw key state in a drag event.</summary>
    /// <remarks>
    /// During a drag the source application owns the input stream, so no key event
    /// reaches us — but Shift and Ctrl still decide copy from move, which is why the
    /// drag events carry their own key state.
    /// </remarks>
    internal static Core.Events.KeyModifiers ToModifiers(Keys keys)
    {
        var modifiers = Core.Events.KeyModifiers.None;
        if ((keys & Keys.Shift) != 0) modifiers |= Core.Events.KeyModifiers.Shift;
        if ((keys & Keys.Control) != 0) modifiers |= Core.Events.KeyModifiers.Ctrl;
        if ((keys & Keys.Alt) != 0) modifiers |= Core.Events.KeyModifiers.Alt;
        return modifiers;
    }

    /// <summary>The modifier keys out of a <see cref="DragEventArgs"/>'s raw state.</summary>
    /// <remarks>
    /// <see cref="DragEventArgs.KeyState"/> is the OLE bit set, not <see cref="Keys"/>:
    /// bit 2 is Shift, bit 3 Ctrl, bit 5 Alt.
    /// </remarks>
    internal static Keys KeysFrom(int oleKeyState)
    {
        var keys = Keys.None;
        if ((oleKeyState & 0x0004) != 0) keys |= Keys.Shift;
        if ((oleKeyState & 0x0008) != 0) keys |= Keys.Control;
        if ((oleKeyState & 0x0020) != 0) keys |= Keys.Alt;
        return keys;
    }
}
