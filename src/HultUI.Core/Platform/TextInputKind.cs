namespace HultUI.Core.Platform;

/// <summary>What a focused field tells the platform's text input about itself.</summary>
public enum TextInputKind
{
    /// <summary>Ordinary text: input methods, prediction and on-screen keyboards as the
    /// platform likes them.</summary>
    Text,

    /// <summary>A password. The platform is asked to keep input methods out of it — no
    /// composition, and nothing learned or predicted from what is typed — as it does for its
    /// own password boxes.</summary>
    Password,
}
