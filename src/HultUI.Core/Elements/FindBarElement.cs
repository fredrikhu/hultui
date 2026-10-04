using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>
/// The row <see cref="Builders.FindBar.Build"/> returns. It exists to put
/// <see cref="FindBarTheme"/> on itself and its parts each style pass, the way every themed
/// control takes its theme — a builder runs before any theme is known.
/// </summary>
/// <remarks>
/// The parts are found by their automation ids rather than held: a rebuild patches the live bar
/// from a fresh one, and a reference to a part would go on naming the fresh bar's part, which is
/// thrown away. The state the theme depends on is copied across in <see cref="PatchFrom"/>.
/// </remarks>
public sealed class FindBarElement : BoxElement
{
    internal bool StatusIsBad { get; set; }
    internal bool MatchCase { get; set; }
    internal bool UseRegex { get; set; }

    protected internal override void ApplyTheme(Theme theme)
    {
        var t = theme.Get<FindBarTheme>();
        BackgroundColor = t.Background ?? theme.Surface;
        BorderColor = t.Divider ?? theme.Border;
        Box.BorderWidth = t.DividerWidth;
        Box.Padding = t.Padding;
        Flex.Gap = t.Gap;

        // The parts are children, and the style pass reaches them after this: what is written
        // into them here is what they resolve.
        foreach (var part in Children)
        {
            switch (part.AutomationId)
            {
                case "find-input" when part is TextInputElement field:
                    field.Style = t.Field;
                    if (t.FieldFontFamily is { } fieldFamily) field.Inherited.FontFamily = fieldFamily;
                    break;
                case "find-status":
                    part.Inherited.FontSize = t.StatusFontSize;
                    part.Inherited.TextColor = StatusIsBad
                        ? t.StatusErrorColor ?? theme.Danger
                        : t.StatusColor ?? theme.Secondary;
                    if (t.StatusFontFamily is { } statusFamily) part.Inherited.FontFamily = statusFamily;
                    break;
                case "find-case" when part is ButtonElement chip:
                    Chip(chip, MatchCase, t);
                    break;
                case "find-regex" when part is ButtonElement chip:
                    Chip(chip, UseRegex, t);
                    break;
                case "find-prev" or "find-next" or "find-close" when part is ButtonElement button:
                    button.Style = t.Button;
                    break;
            }
        }
    }

    private static void Chip(ButtonElement chip, bool on, FindBarTheme t)
    {
        chip.Variant = on ? ButtonVariant.Primary : ButtonVariant.Secondary;
        chip.Style = on ? t.ToggleOn : t.Toggle;
        if (t.ToggleFontFamily is { } family) chip.Inherited.FontFamily = family;
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is FindBarElement bar)
        {
            StatusIsBad = bar.StatusIsBad;
            MatchCase = bar.MatchCase;
            UseRegex = bar.UseRegex;
        }
    }
}
