using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Reactivity;
using HultUI.Core.RichText;
using HultUI.RichText.Markdown;
using Junction.Theme;

namespace Junction;

/// <summary>
/// The Markdown editors — a merge request's description, a review comment, a reply: rich text
/// offering only Markdown's formats, since GitLab is sent Markdown, under one formatting bar.
/// </summary>
public partial class JunctionApp
{
    private static readonly IReadOnlyList<RichTextCommand?> MarkdownBarCommands =
    [
        RichTextCommand.Bold, RichTextCommand.Italic, RichTextCommand.Strike, RichTextCommand.Code,
        RichTextCommand.Link, null,
        RichTextCommand.Heading2, RichTextCommand.BulletList, RichTextCommand.NumberedList,
        RichTextCommand.TaskList, RichTextCommand.Quote, RichTextCommand.CodeBlock, null,
        RichTextCommand.Table,
    ];

    /// <summary>The formatting bar of the editor <paramref name="editor"/> points at.</summary>
    private RichTextToolbar MarkdownBar(Ref<RichTextEditorElement> editor) => new(editor, MarkDirty)
    {
        Commands = MarkdownBarCommands,
        ButtonSize = 26f,
        Icon = command => command switch
        {
            RichTextCommand.Link => Ui.Icon(Icons.LinkSimple, 14f, Nocturne.On(0.8)),
            RichTextCommand.Code => Ui.Icon(Icons.Code, 14f, Nocturne.On(0.8)),
            RichTextCommand.TaskList => Ui.Icon(Icons.ListChecks, 14f, Nocturne.On(0.8)),
            RichTextCommand.Table => Ui.Icon(Icons.Table, 14f, Nocturne.On(0.8)),
            _ => null,
        },
    };

    /// <summary>An editor of <paramref name="document"/> as Markdown, its links edited through
    /// <paramref name="bar"/>. Pictures load as the Markdown view of <paramref name="repoPath"/>
    /// loads them: uploads through the account, the rest plainly, one cache for both. One
    /// arriving rebuilds, and the editor asks again.</summary>
    /// <param name="changed">The document as typed.</param>
    /// <param name="selectionChanged">The caret or the selection moved — so the bar's buttons
    /// follow it. A typed character raises both, in one event: hand both a rebuild that
    /// coalesces (a signal write, <see cref="RerenderView"/>), not <c>MarkDirty</c>, which
    /// builds at once and so built every character twice.</param>
    private RichTextEditorElement MarkdownEditor(RichDocument document, RichTextToolbar bar, string repoPath,
        Action<RichDocument> changed, Action selectionChanged)
    {
        var cx = MdContextFor(repoPath);
        return RichTextEditor.New(document)
            .Features(RichMarkdown.Features)
            .OnDocumentChanged(changed)
            .OnSelectionChanged(selectionChanged)
            .OnLinkRequested(bar.OpenLinkEditor)
            .OnLinkActivated(OpenUrl)
            .Images(url => MdPictureFor(ResolveMarkdownUrl(url, cx.Remote), cx).Source)
            .TextColor(Nocturne.Text);
    }
}
