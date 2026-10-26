using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace PerfSuite;

static partial class Scenarios
{
    /// <summary>
    /// A character typed into a summary field whose change rebuilds the one component holding a
    /// commit form — a description <see cref="TextAreaElement"/> and two checkboxes, as Junction's
    /// Changes view has — beside a 150-line diff on item rows and a 19,000-line response in a
    /// read-only TextArea, as Meridian's URL field rebuilds the pane around its response. The
    /// rebuild patches every one of them with what it already shows: what the keystroke lays out
    /// is what those patches lay out, with every box around them and what those boxes measure.
    /// </summary>
    private static Op TypeBesideTextAreaOp(Ctx ctx)
    {
        var text = Ui.JsonResponse(ctx.Rng, JsonLines);
        var pane = new CommitBesideResponse(ctx.Rng, text, Ui.JsonSpans(text));
        ctx.Mount(pane.Mount());
        ctx.Window.Click(pane.Input!);
        ctx.Pump(2);
        var response = pane.Response!;
        var amend = pane.Amend!;
        int buildsAtStart = pane.Builds;
        return new Op
        {
            Run = step => ctx.Window.TypeText(((char)('a' + step % 26)).ToString()),
            Check = step =>
            {
                if (pane.Summary.Length != step + 1)
                    return $"the summary holds {pane.Summary.Length} characters after {step + 1} typed";
                if (pane.Builds - buildsAtStart != step + 1)
                    return $"{pane.Builds - buildsAtStart} rebuilds for {step + 1} characters typed";
                if (!ReferenceEquals(pane.Response, response) || !ReferenceEquals(pane.Amend, amend))
                    return "the rebuild replaced the response or a checkbox instead of patching it";
                return JsonShown(ctx, response, text);
            },
        };
    }

    /// <summary>
    /// A commit form beside a diff and a response, made by one component whose Build every
    /// keystroke in the summary reruns (the field writes the summary and marks the component
    /// dirty): the summary field, a description text area, two checkboxes and a button on the
    /// left; on the right a file's line over the diff, on kept item rows as Junction's are, and
    /// the response, read-only and monospace in Meridian's colours.
    /// </summary>
    private sealed class CommitBesideResponse(Random rng, string body, TextSpan[] spans) : Component
    {
        private readonly List<DiffLine> _rows = TypeBesideDiff.Flatten(TypeBesideDiff.MakeSource(rng, 150));
        private readonly Ref<TextInputElement> _input = new();
        private readonly Ref<TextAreaElement> _response = new();
        private readonly Ref<CheckboxElement> _amend = new();
        private string _description = "";
        private bool _amendChecked, _signOff = true;

        public string Summary { get; private set; } = "";

        public int Builds { get; private set; }

        public TextInputElement? Input => _input.Current;

        public TextAreaElement? Response => _response.Current;

        public CheckboxElement? Amend => _amend.Current;

        public override Element Build()
        {
            Builds++;
            var summary = TextInput.New(Summary).PlaceholderText("Summary (required)")
                .OnTextChanged(text => { Summary = text; MarkDirty(); })
                .Height(Dimension.Px(34)).WithRef(_input);
            summary.Key = "summary";   // keeps its focus and caret through the rebuild

            var description = TextArea.New(_description).PlaceholderText("Description")
                .OnTextChanged(text => _description = text)
                .Height(Dimension.Px(120));

            var diff = VirtualListElement<DiffLine>.New()
                .ItemHeight(TypeBesideDiff.RowHeight)
                .Items(_rows)
                .RenderWith((row, _) => TypeBesideDiff.DiffRow(row))
                .FlexGrow(1);

            var response = JsonViewer();
            response.Text = body;
            response.Spans = spans;

            return Flex.Row(gap: 0).FlexGrow(1).Children(
                Flex.Column(gap: 8).Width(Dimension.Px(320)).FlexShrink(0).Padding(12)
                    .BorderColor(Ui.Line).BorderWidth(new Thickness(0, 1, 0, 0))
                    .Children(
                        Text.New("Commit").FontSize(13f).FontWeight(FontWeight.SemiBold),
                        summary,
                        description,
                        Checkbox.New(_amendChecked, "Amend the last commit")
                            .OnCheckedChanged(on => { _amendChecked = on; MarkDirty(); }).WithRef(_amend),
                        Checkbox.New(_signOff, "Sign off")
                            .OnCheckedChanged(on => { _signOff = on; MarkDirty(); }),
                        Button.New("Commit")),
                Flex.Column(gap: 0).FlexGrow(1).Children(
                    Flex.Row(gap: 12).FlexShrink(0).AlignItems(AlignItems.Center).Padding(new Thickness(6, 12)).Children(
                        Text.New("README.md").FontSize(12f).FlexGrow(1),
                        Text.New("200 OK").FontSize(12f).TextColor(Ui.Accent)),
                    Flex.Row(gap: 0).FlexGrow(1).Children(
                        diff.FlexBasis(Dimension.Px(0)), response.FlexBasis(Dimension.Px(0)).WithRef(_response))));
        }
    }
}
