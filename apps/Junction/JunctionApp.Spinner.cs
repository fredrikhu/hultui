using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using Junction.Theme;

namespace Junction;

/// <summary>
/// The spinner every busy button and waiting card shows — a circle-notch that turns. One
/// phase for all of them, driven by a looping tween that runs only while a build asked for
/// a spinner. A step turns the spinners on screen, through a Ref each: a repaint. It called
/// MarkDirty, and every frame a spinner was up rebuilt the whole window (about 215 KB a
/// frame in History, 12.8 MB a second). A build turns the ones it makes to the phase too.
/// </summary>
public partial class JunctionApp
{
    private float _spinnerPhase;
    private bool _spinnerWanted;
    private TweenHandle _spinnerTween;
    private Action<float>? _spinnerStep;
    /// <summary>One Ref per spinner the last build made, in the order it made them; kept from
    /// build to build, so a build makes none once it has made as many before.</summary>
    private readonly List<Ref<Element>> _spinners = [];
    private int _spinnersBuilt;

    /// <summary>Before a build: it asks for spinners afresh.</summary>
    private void ResetSpinners()
    {
        _spinnerWanted = false;
        _spinnersBuilt = 0;
    }

    /// <summary>A turning circle-notch. Asking for one keeps the phase running until a
    /// build asks for none.</summary>
    private Element Spinner(float size, Color color)
    {
        _spinnerWanted = true;
        if (_spinnersBuilt == _spinners.Count) _spinners.Add(new Ref<Element>());
        var spinner = _spinners[_spinnersBuilt++];
        return Ui.Icon(Icons.CircleNotch, size, color).Transform(Transform2D.Rotate(_spinnerPhase * 360f))
            .WithRef(spinner);
    }

    /// <summary>A step of the phase: every spinner on screen turned to it.</summary>
    private void TurnSpinners(float phase)
    {
        _spinnerPhase = phase;
        var turn = Transform2D.Rotate(phase * 360f);
        for (int i = 0; i < _spinnersBuilt; i++)
            if (_spinners[i].Current is { } spinner) spinner.Transform = turn;
    }

    /// <summary>A button that has become its own progress: the label of what is running,
    /// muted, with the spinner where the glyph was.</summary>
    private BoxElement BusyButton(string label)
    {
        var muted = Nocturne.On(0.32);
        return Flex.Row(gap: 6).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Padding(new Thickness(5, 10, 5, 10)).CornerRadius(Nocturne.RadiusMd)
            .BorderColor(Nocturne.On(0.18)).BorderWidth(1f).Cursor(CursorType.Default)
            .Children(
                Spinner(13.5f, muted),
                Text.New(label).FontSize(12.5f).FontWeight(FontWeight.Medium).TextColor(muted));
    }

    /// <summary>After a build: start the phase when a spinner is on screen, stop it when
    /// none is. Called from OnAfterBuild; Build resets the flag before building.</summary>
    private void ServiceSpinner()
    {
        // The spinners an earlier build made and this one did not: let them go.
        for (int i = _spinnersBuilt; i < _spinners.Count; i++) _spinners[i].Current = null;

        bool running = !_spinnerTween.IsComplete;
        if (_spinnerWanted && !running)
        {
            _spinnerTween = Animate.Value(0f, 1f, _spinnerStep ??= TurnSpinners)
                .Duration(0.9f)
                .Ease(Easing.Linear)
                .Loop()
                .Start();
        }
        else if (!_spinnerWanted && running)
        {
            _spinnerTween.Cancel();
            _spinnerTween = default;
            _spinnerPhase = 0f;
        }
    }
}
