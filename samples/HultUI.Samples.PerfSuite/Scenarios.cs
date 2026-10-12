namespace PerfSuite;

/// <summary>
/// Every scenario, in report order. A scenario's name is "group/what"; keep a name stable
/// once captures exist, because <c>--compare</c> matches rows by name.
/// </summary>
static partial class Scenarios
{
    public static IEnumerable<Scenario> All() =>
        Layout()
            .Concat(TextScenarios())
            .Concat(Scroll())
            .Concat(Virtual())
            .Concat(Components())
            .Concat(Input())
            .Concat(RichTextScenarios())
            .Concat(Overlays())
            .Concat(WindowScenarios())
            .Concat(Controls())
            .Concat(Paint())
            .Concat(Accessibility())
            .Concat(MediaScenarios())
            .Concat(LoopScenarios());
}
