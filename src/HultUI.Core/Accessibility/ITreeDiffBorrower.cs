namespace HultUI.Core.Accessibility;

/// <summary>
/// A bridge that is done with the <see cref="TreeDiff"/> it is handed when
/// <see cref="IAccessibilityBridge.ApplyChanges"/> or <see cref="IAccessibilityBridge.ApplyPatchedProperties"/>
/// returns: it reads the diff during the call and keeps no reference to it or its lists. The window
/// lends such a bridge one diff of its own for each patch and in-place refresh, emptied after the
/// call, instead of a new one each time (four lists and the changes' array, about 280 bytes a
/// keystroke). The UIA and AT-SPI bridges are borrowers. A bridge that keeps diffs (the headless
/// one records them) is not, and gets a new one every time; so does any other bridge.
/// </summary>
internal interface ITreeDiffBorrower { }
