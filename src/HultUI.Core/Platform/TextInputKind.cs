namespace HultUI.Core.Platform;

/// <summary>
/// What a focused field tells the platform's text input about itself. On a phone it picks the
/// soft keyboard (digits for a number, <c>@</c> for an email) and whether the keyboard
/// capitalizes and corrects; on the desktop only <see cref="Password"/> changes anything
/// (docs/design/SOFT-KEYBOARD.md §2).
/// </summary>
public enum TextInputKind
{
    /// <summary>Ordinary text: input methods, prediction and on-screen keyboards as the
    /// platform likes them. A phone capitalizes sentences and corrects.</summary>
    Text,

    /// <summary>A password. The platform is asked to keep input methods out of it — no
    /// composition, and nothing learned or predicted from what is typed — as it does for its
    /// own password boxes.</summary>
    Password,

    /// <summary>A whole number, zero or more: a keyboard of digits.</summary>
    Number,

    /// <summary>A number that may have a fraction or a sign: digits, the decimal point and minus.</summary>
    Decimal,

    /// <summary>A telephone number: the dial pad.</summary>
    Phone,

    /// <summary>An email address: a keyboard with <c>@</c>, no capitals, no correction.</summary>
    Email,

    /// <summary>A web address: a keyboard with <c>/</c> and <c>.com</c>, no capitals, no correction.</summary>
    Url,

    /// <summary>A user name: no capitals, no correction.</summary>
    Username,

    /// <summary>A person's name: each word capitalized, no correction.</summary>
    Name,

    /// <summary>A search query: no capitals, with correction.</summary>
    Search,
}
