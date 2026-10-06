using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.RichText;
using HultUI.Core.Styling;

namespace HultUI.Core.Builders;

/// <summary>What a toolbar button does.</summary>
public enum RichTextCommand
{
    Undo, Redo,
    Bold, Italic, Underline, Strike, Code, Link, ClearFormatting,
    Paragraph, Heading1, Heading2, Heading3,
    BulletList, NumberedList, TaskList, Quote, CodeBlock, Rule,
    AlignStart, AlignCenter, AlignEnd,
    Outdent, Indent,
}

/// <summary>
/// A formatting toolbar for a <see cref="RichTextEditorElement"/>: a button per command, pressed
/// while the selection has that format, and only the commands the editor's
/// <see cref="RichTextFeatures"/> allow. The link button opens an address editor against itself.
/// </summary>
/// <remarks>
/// <para>Like <see cref="FindBar"/>, an object the app keeps and builds each frame: hand it the
/// editor's <see cref="Ref{T}"/> and a callback that rebuilds, and wire the editor's
/// <see cref="RichTextEditorElement.SelectionChanged"/> to the same callback so the buttons follow
/// the caret. Every command is also on <see cref="Execute"/>, for an app that draws its own.</para>
/// <para>Build it <i>before</i> attaching the ref to the freshly built editor:
/// <see cref="Element.WithRef{T}"/> points the ref at the new element at once, and that one is
/// not the editor on screen until the rebuild is reconciled — its selection is a fresh one.</para>
/// <para>Core takes no icon library (docs/ICONS.md), so buttons are labelled with text — B, I, U,
/// S, H1 — unless <see cref="Icon"/> hands in artwork.</para>
/// </remarks>
public sealed class RichTextToolbar
{
    private readonly Ref<RichTextEditorElement> _editor;
    private readonly Action _onChanged;
    private readonly Ref<ButtonElement> _linkButton = new();
    private readonly Ref<TextInputElement> _linkField = new();
    private readonly Popover _linkPopover;
    private string _linkDraft = "";

    public RichTextToolbar(Ref<RichTextEditorElement> editor, Action onChanged)
    {
        _editor = editor;
        _onChanged = onChanged;
        _linkPopover = new Popover(BuildLinkEditor)
        {
            AccessibleName = "Link",
            AutomationId = "richtext-link-editor",
            Closed = () => { _editor.Current?.Focus(); _onChanged(); },
        };
    }

    /// <summary>The buttons, in order; null draws a divider.</summary>
    public IReadOnlyList<RichTextCommand?> Commands { get; set; } = Default;

    public static readonly IReadOnlyList<RichTextCommand?> Default =
    [
        RichTextCommand.Undo, RichTextCommand.Redo, null,
        RichTextCommand.Heading1, RichTextCommand.Heading2, RichTextCommand.Heading3, null,
        RichTextCommand.Bold, RichTextCommand.Italic, RichTextCommand.Underline, RichTextCommand.Strike,
        RichTextCommand.Code, RichTextCommand.Link, null,
        RichTextCommand.BulletList, RichTextCommand.NumberedList, RichTextCommand.TaskList,
        RichTextCommand.Quote, RichTextCommand.CodeBlock, RichTextCommand.Rule, null,
        RichTextCommand.AlignStart, RichTextCommand.AlignCenter, RichTextCommand.AlignEnd,
    ];

    /// <summary>Artwork for a command's button; null, or a null result, keeps its text label.</summary>
    public Func<RichTextCommand, Element?>? Icon { get; set; }

    public float ButtonSize { get; set; } = 28f;

    /// <summary>Whether the link editor is open.</summary>
    public bool IsLinkEditorOpen => _linkPopover.IsOpen;

    public Element Build()
    {
        var editor = _editor.Current;
        var row = Flex.Row(gap: 2).AlignItems(AlignItems.Center).FlexWrap(FlexWrap.Wrap)
            .AccessibleName("Formatting").AutomationId("richtext-toolbar");
        row.Role = Accessibility.AccessibilityRole.Group;

        bool lastWasDivider = true;
        foreach (var command in Commands)
        {
            if (command is not { } c)
            {
                if (!lastWasDivider) row.AddChild(Divider());
                lastWasDivider = true;
                continue;
            }
            if (editor is not null && !IsAvailable(editor, c)) continue;
            row.AddChild(BuildButton(editor, c));
            lastWasDivider = false;
        }
        return row;
    }

    private Element Divider() =>
        Box.New().Width(1).Height(ButtonSize - 8).Margin(new Thickness(0, 4, 0, 4))
            .Background(new Color(128, 128, 128, 90));

    private ButtonElement BuildButton(RichTextEditorElement? editor, RichTextCommand command)
    {
        bool active = editor is not null && IsActive(editor, command);
        bool enabled = editor is not null && !editor.ReadOnly && IsEnabled(editor, command);
        var button = Button.New(active ? ButtonVariant.Secondary : ButtonVariant.Ghost)
            .Width(Dimension.Px(ButtonSize)).Height(Dimension.Px(ButtonSize)).Padding(0)
            .Enabled(enabled)
            .Tooltip(TooltipOf(command))
            .AccessibleName(NameOf(command))
            .AutomationId("richtext-" + command.ToString().ToLowerInvariant());
        button.AccessibleToggled = IsToggle(command) ? active : null;

        if (Icon?.Invoke(command) is { } icon)
            button.AddChild(icon);
        else
            button.AddChild(Label(command));

        button.OnClick(_ =>
        {
            if (_editor.Current is not { } e) return;
            if (command == RichTextCommand.Link)
            {
                OpenLinkEditor();
                return;
            }
            Execute(e, command);
            e.Focus();
            _onChanged();
        });
        if (command == RichTextCommand.Link) button.WithRef(_linkButton);
        return button;
    }

    // B in bold, I in italic, U underlined, S struck: the label shows what the button does.
    private static Element Label(RichTextCommand command)
    {
        var text = Text.New(LabelOf(command)).FontSize(13);
        return command switch
        {
            RichTextCommand.Bold => text.FontWeight(FontWeight.Bold),
            RichTextCommand.Italic => text.Italic(),
            RichTextCommand.Underline => text.Underline(),
            RichTextCommand.Strike => text.Strikethrough(),
            RichTextCommand.Code or RichTextCommand.CodeBlock => text.FontFamily("monospace"),
            RichTextCommand.Heading1 or RichTextCommand.Heading2 or RichTextCommand.Heading3 => text.FontWeight(FontWeight.SemiBold),
            _ => text,
        };
    }

    // ── The link editor ──

    /// <summary>Opens the address editor against the link button: filled with the link the caret
    /// is in, if any. What Ctrl+K should do — wire it with
    /// <see cref="RichTextEditorExtensions.OnLinkRequested"/>.</summary>
    public void OpenLinkEditor()
    {
        if (_editor.Current is not { } e || _linkButton.Current is not { } anchor) return;
        _linkDraft = e.State.LinkRangeAt(e.State.Selection.Focus)?.Target ?? e.State.SelectionMarks.Link ?? "";
        _linkPopover.Open(anchor);
        _onChanged();
    }

    private Element BuildLinkEditor()
    {
        var field = TextInput.New(_linkDraft)
            .PlaceholderText("https://")
            .OnTextChanged(t => _linkDraft = t)
            .Width(Dimension.Px(260))
            .AccessibleName("Address")
            .AutomationId("richtext-link-address");
        field.OnKeyDown(k =>
        {
            if (k.Key == KeyCode.Enter) { ApplyLink(); k.StopPropagation = true; }
        });
        field.WithRef(_linkField);

        bool inLink = _editor.Current is { } e && e.State.LinkRangeAt(e.State.Selection.Focus) is not null;
        var apply = Button.New("Apply").OnClick(_ => ApplyLink()).AutomationId("richtext-link-apply");
        var remove = Button.New("Remove", ButtonVariant.Secondary).Enabled(inLink)
            .OnClick(_ => { _editor.Current?.State.SetLink(null); _linkPopover.Close(); })
            .AutomationId("richtext-link-remove");

        return Flex.Row(gap: 8).AlignItems(AlignItems.Center).Padding(10).Children(field, apply, remove);
    }

    // With a selection, the selection becomes the link; inside a link, the link changes; at a
    // bare caret, the address goes in as its own text.
    private void ApplyLink()
    {
        string target = _linkDraft.Trim();
        if (_editor.Current is { } e && target.Length > 0)
        {
            var s = e.State;
            if (!s.SetLink(target) && s.Selection.IsCollapsed)
                s.InsertLink(target, target);
        }
        _linkPopover.Close();
    }

    // ── Commands ──

    /// <summary>Whether the editor's features allow <paramref name="command"/>.</summary>
    public static bool IsAvailable(RichTextEditorElement editor, RichTextCommand command)
    {
        var f = editor.Features;
        return command switch
        {
            RichTextCommand.Bold => f.Allows(RichTextFeatures.Bold),
            RichTextCommand.Italic => f.Allows(RichTextFeatures.Italic),
            RichTextCommand.Underline => f.Allows(RichTextFeatures.Underline),
            RichTextCommand.Strike => f.Allows(RichTextFeatures.Strike),
            RichTextCommand.Code => f.Allows(RichTextFeatures.InlineCode),
            RichTextCommand.Link => f.Allows(RichTextFeatures.Links),
            RichTextCommand.Heading1 or RichTextCommand.Heading2 or RichTextCommand.Heading3 => f.Allows(RichTextFeatures.Headings),
            RichTextCommand.BulletList or RichTextCommand.NumberedList => f.Allows(RichTextFeatures.Lists),
            RichTextCommand.TaskList => f.Allows(RichTextFeatures.TaskLists),
            RichTextCommand.Quote => f.Allows(RichTextFeatures.Quotes),
            RichTextCommand.CodeBlock => f.Allows(RichTextFeatures.CodeBlocks),
            RichTextCommand.Rule => f.Allows(RichTextFeatures.Rules),
            RichTextCommand.AlignStart or RichTextCommand.AlignCenter or RichTextCommand.AlignEnd => f.Allows(RichTextFeatures.Alignment),
            _ => true,
        };
    }

    private static bool IsEnabled(RichTextEditorElement editor, RichTextCommand command) => command switch
    {
        RichTextCommand.Undo => editor.State.CanUndo,
        RichTextCommand.Redo => editor.State.CanRedo,
        _ => true,
    };

    private static bool IsToggle(RichTextCommand command) =>
        command is not (RichTextCommand.Undo or RichTextCommand.Redo or RichTextCommand.Rule
            or RichTextCommand.Outdent or RichTextCommand.Indent or RichTextCommand.ClearFormatting);

    /// <summary>Whether the selection already has what <paramref name="command"/> makes: a
    /// button drawn pressed.</summary>
    public static bool IsActive(RichTextEditorElement editor, RichTextCommand command)
    {
        var s = editor.State;
        var marks = s.SelectionMarks;
        return command switch
        {
            RichTextCommand.Bold => marks.Has(InlineStyle.Bold),
            RichTextCommand.Italic => marks.Has(InlineStyle.Italic),
            RichTextCommand.Underline => marks.Has(InlineStyle.Underline),
            RichTextCommand.Strike => marks.Has(InlineStyle.Strike),
            RichTextCommand.Code => marks.Has(InlineStyle.Code),
            RichTextCommand.Link => marks.Link is not null || s.LinkRangeAt(s.Selection.Focus) is not null && s.Selection.IsCollapsed,
            RichTextCommand.Paragraph => s.SelectionIs(BlockKind.Paragraph),
            RichTextCommand.Heading1 => s.SelectionIs(BlockKind.Heading, 1),
            RichTextCommand.Heading2 => s.SelectionIs(BlockKind.Heading, 2),
            RichTextCommand.Heading3 => s.SelectionIs(BlockKind.Heading, 3),
            RichTextCommand.BulletList => s.SelectionIs(BlockKind.BulletItem),
            RichTextCommand.NumberedList => s.SelectionIs(BlockKind.NumberedItem),
            RichTextCommand.TaskList => s.SelectionIs(BlockKind.TaskItem),
            RichTextCommand.Quote => s.SelectionIs(BlockKind.Quote),
            RichTextCommand.CodeBlock => s.SelectionIs(BlockKind.CodeBlock),
            RichTextCommand.AlignStart => s.CurrentBlock.Align == BlockAlign.Start,
            RichTextCommand.AlignCenter => s.CurrentBlock.Align == BlockAlign.Center,
            RichTextCommand.AlignEnd => s.CurrentBlock.Align == BlockAlign.End,
            _ => false,
        };
    }

    /// <summary>Does <paramref name="command"/> to the editor's selection. The link command needs
    /// an address, which only the toolbar's editor (or the app's) can ask for: here it removes
    /// the link the selection is in, if any.</summary>
    public static bool Execute(RichTextEditorElement editor, RichTextCommand command)
    {
        if (editor.ReadOnly) return false;
        var s = editor.State;
        return command switch
        {
            RichTextCommand.Undo => s.Undo(),
            RichTextCommand.Redo => s.Redo(),
            RichTextCommand.Bold => s.ToggleStyle(InlineStyle.Bold),
            RichTextCommand.Italic => s.ToggleStyle(InlineStyle.Italic),
            RichTextCommand.Underline => s.ToggleStyle(InlineStyle.Underline),
            RichTextCommand.Strike => s.ToggleStyle(InlineStyle.Strike),
            RichTextCommand.Code => s.ToggleStyle(InlineStyle.Code),
            RichTextCommand.Link => s.SetLink(null),
            RichTextCommand.ClearFormatting => s.ClearFormatting(),
            RichTextCommand.Paragraph => s.SetBlockKind(BlockKind.Paragraph),
            RichTextCommand.Heading1 => s.ToggleBlockKind(BlockKind.Heading, 1),
            RichTextCommand.Heading2 => s.ToggleBlockKind(BlockKind.Heading, 2),
            RichTextCommand.Heading3 => s.ToggleBlockKind(BlockKind.Heading, 3),
            RichTextCommand.BulletList => s.ToggleBlockKind(BlockKind.BulletItem),
            RichTextCommand.NumberedList => s.ToggleBlockKind(BlockKind.NumberedItem),
            RichTextCommand.TaskList => s.ToggleBlockKind(BlockKind.TaskItem),
            RichTextCommand.Quote => s.ToggleBlockKind(BlockKind.Quote),
            RichTextCommand.CodeBlock => s.ToggleBlockKind(BlockKind.CodeBlock),
            RichTextCommand.Rule => s.InsertRule(),
            RichTextCommand.AlignStart => s.SetAlign(BlockAlign.Start),
            RichTextCommand.AlignCenter => s.SetAlign(BlockAlign.Center),
            RichTextCommand.AlignEnd => s.SetAlign(BlockAlign.End),
            RichTextCommand.Outdent => s.Outdent(),
            RichTextCommand.Indent => s.Indent(),
            _ => false,
        };
    }

    public static string LabelOf(RichTextCommand command) => command switch
    {
        RichTextCommand.Undo => "↶",
        RichTextCommand.Redo => "↷",
        RichTextCommand.Bold => "B",
        RichTextCommand.Italic => "I",
        RichTextCommand.Underline => "U",
        RichTextCommand.Strike => "S",
        RichTextCommand.Code => "<>",
        RichTextCommand.Link => "🔗",
        RichTextCommand.ClearFormatting => "Tx",
        RichTextCommand.Paragraph => "¶",
        RichTextCommand.Heading1 => "H1",
        RichTextCommand.Heading2 => "H2",
        RichTextCommand.Heading3 => "H3",
        RichTextCommand.BulletList => "•",
        RichTextCommand.NumberedList => "1.",
        RichTextCommand.TaskList => "☐",
        RichTextCommand.Quote => "❝",
        RichTextCommand.CodeBlock => "{}",
        RichTextCommand.Rule => "—",
        RichTextCommand.AlignStart => "⇤",
        RichTextCommand.AlignCenter => "≡",
        RichTextCommand.AlignEnd => "⇥",
        RichTextCommand.Outdent => "⇠",
        RichTextCommand.Indent => "⇢",
        _ => command.ToString(),
    };

    public static string NameOf(RichTextCommand command) => command switch
    {
        RichTextCommand.Strike => "Strikethrough",
        RichTextCommand.Code => "Inline code",
        RichTextCommand.ClearFormatting => "Clear formatting",
        RichTextCommand.Heading1 => "Heading 1",
        RichTextCommand.Heading2 => "Heading 2",
        RichTextCommand.Heading3 => "Heading 3",
        RichTextCommand.BulletList => "Bulleted list",
        RichTextCommand.NumberedList => "Numbered list",
        RichTextCommand.TaskList => "Task list",
        RichTextCommand.CodeBlock => "Code block",
        RichTextCommand.Rule => "Horizontal rule",
        RichTextCommand.AlignStart => "Align left",
        RichTextCommand.AlignCenter => "Centre",
        RichTextCommand.AlignEnd => "Align right",
        _ => command.ToString(),
    };

    public static string TooltipOf(RichTextCommand command)
    {
        string? keys = command switch
        {
            RichTextCommand.Undo => "Ctrl+Z",
            RichTextCommand.Redo => "Ctrl+Y",
            RichTextCommand.Bold => "Ctrl+B",
            RichTextCommand.Italic => "Ctrl+I",
            RichTextCommand.Underline => "Ctrl+U",
            RichTextCommand.Strike => "Ctrl+Shift+X",
            RichTextCommand.Code => "Ctrl+E",
            RichTextCommand.Link => "Ctrl+K",
            RichTextCommand.Heading1 => "Ctrl+Alt+1",
            RichTextCommand.Heading2 => "Ctrl+Alt+2",
            RichTextCommand.Heading3 => "Ctrl+Alt+3",
            RichTextCommand.Paragraph => "Ctrl+Alt+0",
            RichTextCommand.NumberedList => "Ctrl+Shift+7",
            RichTextCommand.BulletList => "Ctrl+Shift+8",
            RichTextCommand.TaskList => "Ctrl+Shift+9",
            _ => null,
        };
        return keys is null ? NameOf(command) : $"{NameOf(command)} ({keys})";
    }
}
