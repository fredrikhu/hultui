using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using Xunit;

namespace HultUI.Core.Tests.Components;

[Collection("Reactive")]
public class ComponentTests
{
    private class CounterComponent : Component
    {
        public Signal<int> Count { get; } = new(0);
        public int BuildCount { get; private set; }

        public override Element Build()
        {
            BuildCount++;
            return Box.New()
                .Background(new Color(100, 100, 100))
                .Children(Text.New($"Count: {Count.Value}"));
        }
    }

    [Fact]
    public void Mount_BuildsImmediately()
    {
        var component = new CounterComponent();
        var host = component.Mount();

        Assert.Equal(1, component.BuildCount);
        Assert.Single(host.Children); // the BoxElement
        var box = Assert.IsType<BoxElement>(host.Children[0]);
        var text = Assert.IsType<TextElement>(box.Children[0]);
        Assert.Equal("Count: 0", text.Content);
    }

    [Fact]
    public void SignalChange_TriggersRebuild()
    {
        var component = new CounterComponent();
        var host = component.Mount();

        // Keep reference to the existing box element
        var originalBox = host.Children[0];

        component.Count.Value = 5;

        Assert.Equal(2, component.BuildCount);
        // The existing element should be patched, not replaced (same type)
        Assert.Same(originalBox, host.Children[0]);
        var text = Assert.IsType<TextElement>(host.Children[0].Children[0]);
        Assert.Equal("Count: 5", text.Content);
    }

    [Fact]
    public void MultipleSignalChanges_EachTriggersRebuild()
    {
        var component = new CounterComponent();
        var host = component.Mount();

        component.Count.Value = 1;
        component.Count.Value = 2;
        component.Count.Value = 3;

        Assert.Equal(4, component.BuildCount); // 1 initial + 3 changes
        var text = Assert.IsType<TextElement>(host.Children[0].Children[0]);
        Assert.Equal("Count: 3", text.Content);
    }

    [Fact]
    public void BatchedChanges_SingleRebuild()
    {
        var component = new CounterComponent();
        var host = component.Mount();

        ReactiveRuntime.Batch(() =>
        {
            component.Count.Value = 10;
            component.Count.Value = 20;
            component.Count.Value = 30;
        });

        Assert.Equal(2, component.BuildCount); // 1 initial + 1 batched
        var text = Assert.IsType<TextElement>(host.Children[0].Children[0]);
        Assert.Equal("Count: 30", text.Content);
    }

    private class LifecycleComponent : Component
    {
        public List<string> Log { get; } = [];

        public override Element Build()
        {
            Log.Add("Build");
            return Box.New();
        }

        protected internal override void OnMount() => Log.Add("Mount");
        protected internal override void OnUnmount() => Log.Add("Unmount");
        protected internal override void OnBeforeBuild() => Log.Add("BeforeBuild");
        protected internal override void OnAfterBuild() => Log.Add("AfterBuild");
    }

    [Fact]
    public void Lifecycle_BeforeBuild_Build_AfterBuild_OnConstruction()
    {
        var component = new LifecycleComponent();
        _ = component.Mount();

        Assert.Equal(["BeforeBuild", "Build", "AfterBuild"], component.Log);
    }

    [Fact]
    public void Lifecycle_Mount_WhenAddedToTree()
    {
        var component = new LifecycleComponent();
        var host = component.Mount();
        component.Log.Clear();

        var parent = Box.New();
        parent.AddChild(host);

        Assert.Equal(["Mount"], component.Log);
    }

    [Fact]
    public void Lifecycle_Unmount_WhenRemovedFromTree()
    {
        var component = new LifecycleComponent();
        var host = component.Mount();
        var parent = Box.New();
        parent.AddChild(host);
        component.Log.Clear();

        parent.RemoveChild(host);

        Assert.Equal(["Unmount"], component.Log);
    }

    private class ConditionalComponent : Component
    {
        public Signal<bool> ShowHeader { get; } = new(true);

        public override Element Build()
        {
            if (ShowHeader.Value)
            {
                return Flex.Column().Children(
                    Text.New("Header"),
                    Text.New("Body")
                );
            }
            return Flex.Column().Children(
                Text.New("Body")
            );
        }
    }

    [Fact]
    public void ConditionalChildren_AddsAndRemoves()
    {
        var component = new ConditionalComponent();
        var host = component.Mount();

        var column = host.Children[0];
        Assert.Equal(2, column.Children.Count);

        component.ShowHeader.Value = false;
        Assert.Single(column.Children);
        Assert.Equal("Body", ((TextElement)column.Children[0]).Content);

        component.ShowHeader.Value = true;
        Assert.Equal(2, column.Children.Count);
    }

    private class TypeSwitchComponent : Component
    {
        public Signal<bool> UseBox { get; } = new(true);

        public override Element Build()
        {
            if (UseBox.Value)
                return Box.New().Background(Color.Red);
            return Text.New("Fallback");
        }
    }

    [Fact]
    public void TypeChange_ReplacesElement()
    {
        var component = new TypeSwitchComponent();
        var host = component.Mount();

        Assert.IsType<BoxElement>(host.Children[0]);

        component.UseBox.Value = false;
        Assert.IsType<TextElement>(host.Children[0]);
        Assert.Equal("Fallback", ((TextElement)host.Children[0]).Content);

        component.UseBox.Value = true;
        Assert.IsType<BoxElement>(host.Children[0]);
    }

    private class PropertyPatchComponent : Component
    {
        public Signal<string> Color { get; } = new("#FF0000");

        public override Element Build()
        {
            return Box.New()
                .Width(100).Height(50)
                .Background(Primitives.Color.FromHex(Color.Value))
                .Children(Text.New("Hello"));
        }
    }

    [Fact]
    public void PropertyPatch_UpdatesWithoutReplace()
    {
        var component = new PropertyPatchComponent();
        var host = component.Mount();

        var box = (BoxElement)host.Children[0];
        Assert.Equal(Primitives.Color.FromHex("#FF0000"), box.BackgroundColor);

        component.Color.Value = "#00FF00";

        // Same element instance should be reused
        Assert.Same(box, host.Children[0]);
        Assert.Equal(Primitives.Color.FromHex("#00FF00"), box.BackgroundColor);
    }
}
