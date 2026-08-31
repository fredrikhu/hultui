namespace HultUI.Core.Primitives;

/// <summary>
/// A ring drawn outside an element's border box that takes up no room.
///
/// <para>CSS's <c>outline</c>, and it exists for the same reason: a state that adds a border
/// changes the element's size, and an auto-sized element then moves — along with everything
/// packed beside it. Selecting one button in a row nudges its neighbours, and no amount of
/// <c>box-sizing</c> helps, because a box with no declared width is <c>content + padding +
/// border</c> under either box model. There is nothing for a box model to reinterpret.</para>
///
/// <para>The usual workaround is to give the unselected state a transparent border of the same
/// width so both states measure alike. That works and it is what several call sites here do,
/// but it has to be remembered every time, and forgetting looks like a bug in something else.
/// An outline cannot be forgotten wrong: the layout engine never reads it.</para>
///
/// <para><see cref="Offset"/> follows CSS — the gap between the border edge and the ring, and
/// negative values pull it inward, so <c>Offset = -Width</c> puts the ring exactly where a
/// border of that width would have been.</para>
///
/// <para>Being outside the box, a ring can be painted over by a later sibling that overlaps it,
/// and it is clipped by an ancestor that clips — the same terms <see cref="BoxShadow"/> lives
/// on. In a flex row with any gap at all, neither happens.</para>
/// </summary>
/// <param name="Width">Ring thickness. Zero draws nothing.</param>
/// <param name="Color">Ring colour. Fully transparent draws nothing.</param>
/// <param name="Offset">Distance from the border edge outward. Negative pulls the ring in.</param>
public readonly record struct Outline(float Width, Color Color, float Offset = 0f)
{
    /// <summary>Nothing drawn — the default, and what an element has until one is set.</summary>
    public static Outline None => default;

    /// <summary>Whether this would put anything on screen.</summary>
    public bool IsVisible => Width > 0f && Color.A > 0;
}
