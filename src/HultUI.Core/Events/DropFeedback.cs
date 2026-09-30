namespace HultUI.Core.Events;

/// <summary>
/// What the window made of an OS drag at one moment: whether the element under the pointer
/// accepts it. The platform passes one in with the drag's input event, the window fills it in
/// while handling it, and the platform answers the operating system from it — Windows asks a
/// drop target for its answer before the call returns.
/// </summary>
public sealed class DropFeedback
{
    /// <summary>An element accepted the drag at this position, or took the drop.</summary>
    public bool Accepted { get; set; }
}
