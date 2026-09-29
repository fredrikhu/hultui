using Aperture.Core;

namespace Aperture;

/// <summary>
/// Phosphor icon names for the Aperture UI, drawn as real vector artwork by
/// <see cref="HultUI.Icons.Phosphor"/>. Every name is asserted to exist in the bundled set
/// by ApertureIconTests, so a typo fails a test rather than silently rendering nothing.
/// </summary>
public static class Icons
{
    // Rail.
    public const string Home = "house";
    public const string Search = "magnifying-glass";
    public const string Settings = "gear";
    public const string SignOut = "sign-out";

    // Library kinds — the rail's per-library glyph.
    public const string Movies = "film-slate";
    public const string Shows = "television";
    public const string Music = "sparkle";
    public const string Photos = "file-image";
    public const string Books = "browsers";
    public const string OtherLibrary = "folders";

    // Item actions.
    public const string Play = "play";
    public const string Resume = "play-circle";
    public const string Watched = "check-circle";
    public const string Favorite = "heart";
    public const string FavoriteOn = "heart-fill";
    public const string More = "dots-three";
    public const string Back = "arrow-left";
    public const string Episodes = "list";
    public const string Refresh = "arrows-clockwise";
    public const string Loading = "spinner-gap";
    public const string Rating = "star-fill";
    public const string Next = "caret-right";
    public const string Previous = "caret-left";
    public const string Server = "hard-drives";
    public const string User = "user-circle";
    public const string Warning = "warning";

    // The request server.
    public const string Discover = "compass";
    public const string Requests = "list-checks";
    public const string Request = "plus-circle";
    public const string Pending = "clock";
    public const string Processing = "download-simple";
    public const string Available = "check-circle";
    public const string Declined = "x-circle";
    public const string Blocklisted = "prohibit";
    public const string Approve = "check";
    public const string Decline = "x";
    public const string Withdraw = "trash";
    public const string External = "arrow-square-out";

    // Window controls (Windows/Linux style; bold so the marks survive caption size).
    public const string WinMinimize = "minus-bold";
    public const string WinMaximize = "square-bold";
    public const string WinClose = "x-bold";

    /// <summary>The glyph for a library, by what it holds.</summary>
    public static string ForLibrary(LibraryKind kind) => kind switch
    {
        LibraryKind.Movies => Movies,
        LibraryKind.TvShows => Shows,
        LibraryKind.Music or LibraryKind.MusicVideos => Music,
        LibraryKind.Photos => Photos,
        LibraryKind.Books => Books,
        _ => OtherLibrary,
    };

    /// <summary>The glyph standing in for missing artwork, by what the item is.</summary>
    public static string ForItem(MediaKind kind) => kind switch
    {
        MediaKind.Movie => Movies,
        MediaKind.Series or MediaKind.Season or MediaKind.Episode => Shows,
        MediaKind.Person => User,
        MediaKind.BoxSet or MediaKind.Folder => OtherLibrary,
        _ => "film-strip",
    };
}
