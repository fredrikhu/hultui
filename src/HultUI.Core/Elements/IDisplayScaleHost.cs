namespace HultUI.Core.Elements;

/// <summary>
/// A host that knows how many device pixels a layout unit is now: the window. For an element that
/// stops work once what is left of it is too small to show (the scrollbar's hover tween ends when
/// less than half a device pixel of it is left).
/// </summary>
internal interface IDisplayScaleHost
{
    /// <summary>Device pixels per layout unit; 0 or less when not known.</summary>
    float DisplayScale { get; }
}
