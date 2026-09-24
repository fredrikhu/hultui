namespace HultUI.Media;

/// <summary>
/// A chapter the media itself carries: where it starts, and what the file calls it.
///
/// <para>Deliberately not the same type an app uses for a server's chapters — this one
/// says only what a container can say. A player app that has both (a local file's marks
/// and a library's) maps them onto whatever it shows.</para>
/// </summary>
/// <param name="Start">Where the chapter begins.</param>
/// <param name="Title">The file's own title for it, or null when it names none.</param>
public readonly record struct MediaChapterMark(TimeSpan Start, string? Title);
