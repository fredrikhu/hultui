using System.Linq;
using System.Runtime.CompilerServices;
using HultUI.Core.Builders;
using HultUI.Core.DevTools;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Core.Tests.DevTools;

/// <summary>
/// Element origins — the inspector's "Created at" row. The contract has two halves,
/// and both are pinned here: with <c>HultUIElementOrigin</c> defined the origin names
/// the app's call site, and without it there is no origin at all. The second half is
/// not a formality — the whole reason the capture is conditional is that Release
/// builds must carry neither the fields nor the source paths.
/// </summary>
public class ElementOriginTests
{
    /// <summary>The line this is called from. Beats hard-coding a number that shifts on every edit.</summary>
    private static int Here([CallerLineNumber] int line = 0) => line;

#if HULTUI_ELEMENT_ORIGIN

    [Fact]
    public void OriginsAvailable_IsTrueInThisBuild()
    {
        Assert.True(ElementProperties.OriginsAvailable);
    }

    [Fact]
    public void Origin_NamesTheCallSite()
    {
        var element = Box.New();
        int line = Here() - 1;

        var origin = Assert.NotNull(element.Origin);
        Assert.Equal(line, origin.Line);
        Assert.EndsWith("ElementOriginTests.cs", origin.File);
        Assert.Equal("ElementOriginTests.cs", origin.FileName);
        Assert.Equal($"ElementOriginTests.cs:{line}", origin.ToString());
    }

    [Fact]
    public void Origin_NamesTheAppSCallSite_NotTheBuilderThatForwarded()
    {
        // Flex.Row delegates to BoxElement.New. Without the caller info threaded
        // through every layer, every Flex.Row in every app would report Flex.cs —
        // which is the failure mode this whole change exists to avoid.
        var row = Flex.Row(gap: 4);
        int line = Here() - 1;

        var origin = Assert.NotNull(row.Origin);
        Assert.Equal(line, origin.Line);
        Assert.EndsWith("ElementOriginTests.cs", origin.File);
    }

    [Fact]
    public void Origin_IsCapturedByEveryFactoryFamily()
    {
        // A spot check across the builder surface: one that forwards straight
        // through, one that mutates before returning, one that builds a child, and
        // one whose overloads had to be collapsed to keep the label from binding to
        // the file path.
        foreach (var element in new Element[]
                 {
                     Text.New("hi"),
                     ScrollView.Horizontal(),
                     Button.New("Save"),
                     Checkbox.New(true, "Enable"),
                     Grid.Of("1fr 1fr"),
                     TextInput.New("x"),
                 })
        {
            var origin = Assert.NotNull(element.Origin);
            Assert.EndsWith("ElementOriginTests.cs", origin.File);
        }
    }

    [Fact]
    public void CollapsingTheOverloads_KeptTheLabelWorking()
    {
        // The regression this guards: with two overloads, New(true, "Enable") binds
        // "Enable" to the captured file path and the checkbox renders unlabelled.
        var checkbox = Checkbox.New(true, "Enable");

        Assert.Equal("Enable", checkbox.Label);
        Assert.True(checkbox.IsChecked);

        var radio = RadioButton.New("group", true, "Choice");
        Assert.Equal("Choice", radio.Label);
        Assert.Equal("group", radio.GroupName);
        Assert.True(radio.IsChecked);
    }

    [Fact]
    public void Origin_IsNullForAnElementBuiltWithoutAFactory()
    {
        Assert.Null(new BoxElement().Origin);
    }

    [Fact]
    public void Origin_FollowsTheIncomingBuildThroughReconciliation()
    {
        var retained = Box.New();
        var incoming = Box.New();
        int incomingLine = Here() - 1;

        retained.PatchFrom(incoming);

        // A retained element can be reused for a different branch of a conditional
        // build, so its origin has to move with the build that patched it — not stay
        // pinned to whichever branch happened to create it first.
        Assert.Equal(incomingLine, Assert.NotNull(retained.Origin).Line);
    }

    [Fact]
    public void Describe_ReportsTheOriginAsCreatedAt()
    {
        var element = Box.New();
        int line = Here() - 1;

        var group = ElementProperties.Describe(element).First(g => g.Name == "Element");

        Assert.Equal($"ElementOriginTests.cs:{line}", group.Rows.First(r => r.Name == "Created at").Value);
    }

#else

    [Fact]
    public void OriginsAreCompiledOut()
    {
        Assert.False(ElementProperties.OriginsAvailable);
        Assert.Null(Box.New().Origin);
        Assert.Null(Flex.Row().Origin);
    }

    [Fact]
    public void TheOriginRowIsAbsentRatherThanEmpty()
    {
        var group = ElementProperties.Describe(Box.New()).First(g => g.Name == "Element");

        Assert.DoesNotContain(group.Rows, r => r.Name == "Created at");
    }

    [Fact]
    public void TheFactoriesStillWorkWithoutTheCaptureAttributes()
    {
        // The parameters survive in the signature either way; only the attributes
        // are conditional. These calls must still bind their real arguments.
        Assert.Equal("Enable", Checkbox.New(true, "Enable").Label);
        Assert.Equal("Choice", RadioButton.New("group", true, "Choice").Label);
        Assert.Equal("hi", Text.New("hi").Content);
    }

#endif
}
