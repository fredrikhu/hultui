using HultUI.Core.Accessibility;

namespace HultUI.Headless;

/// <summary>
/// Playwright-style web-first assertions: <c>Expect(locator).ToBeVisible()</c> pumps the
/// session's virtual time until the locator meets the expectation, and fails after
/// <see cref="HeadlessSession.LocatorTimeout"/> (or the timeout given) with what was expected,
/// what was found, and the matched elements. <c>using static HultUI.Headless.HeadlessExpect;</c>
/// </summary>
public static class HeadlessExpect
{
    public static LocatorAssertions Expect(Locator locator, TimeSpan? timeout = null) => new(locator, timeout);
}

/// <summary>The assertions on one locator. Each is strict where it reads one element: several
/// matches fail at once, naming them.</summary>
public sealed class LocatorAssertions
{
    private readonly Locator _locator;
    private readonly TimeSpan? _timeout;

    internal LocatorAssertions(Locator locator, TimeSpan? timeout)
    {
        _locator = locator;
        _timeout = timeout;
    }

    public void ToBeVisible() => One("to be visible", Locator.IsVisible, _ => "not visible");

    /// <summary>No element matches, or the one that does is not visible.</summary>
    public void ToBeHidden() => Wait("to be hidden", all => all.Count == 0 || !Locator.IsVisible(all[0]), _ => "visible");

    public void ToBeEnabled() => One("to be enabled", e => e.IsEnabled, _ => "disabled");

    public void ToBeDisabled() => One("to be disabled", e => !e.IsEnabled, _ => "enabled");

    public void ToBeChecked(bool @checked = true)
        => One(@checked ? "to be checked" : "to be unchecked", e => Locator.IsChecked(e) == @checked,
            e => Locator.IsChecked(e) ? "checked" : "unchecked");

    /// <summary>The element's text (its accessible name, else its value) is <paramref name="text"/>,
    /// whitespace normalised.</summary>
    public void ToHaveText(string text)
        => One($"to have text \"{text}\"", e => Locator.Normalize(TextOf(e)) == Locator.Normalize(text), e => $"text \"{TextOf(e)}\"");

    /// <summary>The element's text contains <paramref name="text"/> (ordinal).</summary>
    public void ToContainText(string text)
        => One($"to contain text \"{text}\"", e => TextOf(e).Contains(text, StringComparison.Ordinal), e => $"text \"{TextOf(e)}\"");

    /// <summary>The element's value (a text field's text, a dropdown's choice) is <paramref name="value"/>.</summary>
    public void ToHaveValue(string value)
        => One($"to have value \"{value}\"", e => ValueOf(e) == value, e => $"value \"{ValueOf(e) ?? "(none)"}\"");

    public void ToHaveCount(int count)
        => Wait($"to have count {count}", all => all.Count == count, all => $"count {all.Count}", strict: false);

    public Task ToBeVisibleAsync() => Locator.Run(ToBeVisible);
    public Task ToBeHiddenAsync() => Locator.Run(ToBeHidden);
    public Task ToBeEnabledAsync() => Locator.Run(ToBeEnabled);
    public Task ToBeDisabledAsync() => Locator.Run(ToBeDisabled);
    public Task ToBeCheckedAsync(bool @checked = true) => Locator.Run(() => ToBeChecked(@checked));
    public Task ToHaveTextAsync(string text) => Locator.Run(() => ToHaveText(text));
    public Task ToContainTextAsync(string text) => Locator.Run(() => ToContainText(text));
    public Task ToHaveValueAsync(string value) => Locator.Run(() => ToHaveValue(value));
    public Task ToHaveCountAsync(int count) => Locator.Run(() => ToHaveCount(count));

    private static string TextOf(HeadlessElement e) => e.Name ?? e.Value ?? "";

    // A dropdown's choice is its name (ComboBox nodes carry no value).
    private static string? ValueOf(HeadlessElement e) => e.Value ?? (e.Role == AccessibilityRole.ComboBox ? e.Name : null);

    /// <summary>Exactly one element matches and it satisfies <paramref name="ok"/>.</summary>
    private void One(string expected, Func<HeadlessElement, bool> ok, Func<HeadlessElement, string> found)
        => Wait(expected, all => all.Count == 1 && ok(all[0]),
            all => all.Count == 0 ? "no element" : found(all[0]));

    private void Wait(string expected, Func<IReadOnlyList<HeadlessElement>, bool> ok,
        Func<IReadOnlyList<HeadlessElement>, string> found, bool strict = true)
    {
        var limit = _locator.TimeoutOr(_timeout);
        IReadOnlyList<HeadlessElement> last = [];
        bool met = _locator.PumpUntil(() =>
        {
            last = _locator.All();
            if (strict && last.Count > 1)
                throw _locator.StrictViolation(last);
            return ok(last);
        }, limit);
        if (!met)
            throw new LocatorAssertionException(
                $"Expected locator {_locator} {expected}, but found {found(last)} after {limit.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} s of virtual time.\n" +
                (last.Count == 0 ? "No element matched.\n" : $"Matched:\n{Locator.Dump(last)}"));
    }
}

/// <summary>A <see cref="HeadlessExpect"/> assertion that did not come true in time.</summary>
public sealed class LocatorAssertionException(string message) : Exception(message);
