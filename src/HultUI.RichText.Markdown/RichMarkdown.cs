using HultUI.Core.RichText;

namespace HultUI.RichText.Markdown;

/// <summary>
/// Markdown to and from a <see cref="RichDocument"/> (docs/plans/16-rich-text-editor.md, stage 6):
/// CommonMark, plus the strikethrough and task lists GitLab and GitHub add.
/// </summary>
/// <remarks>
/// <para><b>Reading</b> keeps what the document cannot show: a table, a picture, HTML, a quote
/// holding a list, a link reference definition — each becomes a <see cref="BlockKind.Raw"/>
/// block holding its source, written back exactly. A line break in a paragraph's source reads
/// as a space, as it renders; a hard break (a backslash or two spaces at the end of a line) is a
/// line break. A bare address stays text; <c>&lt;https://…&gt;</c> is a link.</para>
/// <para><b>Writing</b> is canonical, so a document read and written comes back in one
/// spelling: <c>-</c> bullets, numbered items counted from 1, fenced code, <c>**</c> and
/// <c>*</c>, <c>\</c> for a line break. Reading that back gives the same document. What
/// Markdown cannot say — underline, colour, highlight, size, face, alignment, an empty
/// paragraph — is not written; an editor on <see cref="Features"/> offers none of it.</para>
/// </remarks>
public static class RichMarkdown
{
    /// <summary>What an editor of Markdown should offer: <see cref="RichTextFeatures.Markdown"/>.</summary>
    public const RichTextFeatures Features = RichTextFeatures.Markdown;

    public static RichDocument Parse(string? markdown) => RichMarkdownReader.Read(markdown ?? "");

    public static string Write(RichDocument document) => RichMarkdownWriter.Write(document);

    /// <summary>One block's text and marks as inline Markdown, for a caller writing its own
    /// blocks around it.</summary>
    public static string WriteInline(RichBlock block) => RichMarkdownWriter.Inline(block.Text, block.Spans, heading: false);
}
