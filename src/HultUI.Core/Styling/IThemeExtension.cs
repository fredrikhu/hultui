namespace HultUI.Core.Styling;

/// <summary>
/// Marks a type as a themeable knob-set that can live in a <see cref="Theme"/> under its own
/// key, so a control shipped outside this assembly can be themed by an app that never heard
/// of it.
///
/// <para>Implement it on a plain class of <c>init</c> properties, read it from the control's
/// <c>ApplyTheme</c> override with <see cref="Theme.Get{T}"/>, and let apps replace it with
/// <see cref="Theme.With{T}"/>. Resolution is a dictionary lookup at style time — no
/// selectors, no specificity, nothing to trace at runtime.</para>
///
/// <para><see cref="CreateDefault"/> is handed the theme so defaults can be derived from the
/// palette rather than hardcoded. That is the difference between a control that already
/// looks right in a dark theme and one that has to be re-themed by hand for every palette:
/// <c>ButtonTheme</c> carries no colours at all because <c>ButtonElement</c> resolves them
/// from <see cref="Theme.Primary"/> and friends, while <see cref="Theme.Dark"/> has to
/// restate seven literal values for its tooltip and menu. Derive where you can.</para>
///
/// <para>The default is a static member rather than a registration call on purpose: there is
/// no global mutable state, no way to use a control before its library registered anything,
/// and no <see cref="Theme.Get{T}"/> call that can fail for want of a default.</para>
/// </summary>
/// <typeparam name="TSelf">The implementing type itself.</typeparam>
public interface IThemeExtension<out TSelf> where TSelf : IThemeExtension<TSelf>
{
    /// <summary>
    /// The values to use when the app has not overridden this extension. Called at most once
    /// per theme, the first time something asks for it.
    /// </summary>
    static abstract TSelf CreateDefault(Theme theme);
}
