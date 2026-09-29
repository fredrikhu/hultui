using System.Text.Json.Serialization;

namespace Aperture.Core.Requests;

// The wire shapes of the Jellyseerr / Seerr API (/api/v1), as far as Aperture
// reads them. Every field is optional on purpose: the three servers share one API but not
// every field, and a field a server stopped sending must read as "unknown", never as a
// failed page. Nothing here leaves this assembly — SeerrServer maps it all into the app's
// own records (RequestModels.cs) before a view sees it.

internal sealed class SeerrStatusDto
{
    public string? Version { get; set; }
    public string? CommitTag { get; set; }
}

internal sealed class SeerrPublicSettingsDto
{
    public bool? Initialized { get; set; }
    public string? ApplicationTitle { get; set; }
    public bool? Movie4kEnabled { get; set; }
    public bool? Series4kEnabled { get; set; }
    public bool? PartialRequestsEnabled { get; set; }
    /// <summary>1 Plex, 2 Jellyfin, 3 Emby, 4 not configured.</summary>
    public int? MediaServerType { get; set; }
    public bool? MediaServerLogin { get; set; }
    public bool? CacheImages { get; set; }
}

internal sealed class SeerrUserDto
{
    public int Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Username { get; set; }
    public string? JellyfinUsername { get; set; }
    public string? PlexUsername { get; set; }
    public string? Email { get; set; }
    public long Permissions { get; set; }
    /// <summary>The Jellyfin user this account is, in whichever GUID format the server
    /// stored — compared with dashes stripped.</summary>
    public string? JellyfinUserId { get; set; }

    public string Name => FirstNonEmpty(DisplayName, JellyfinUsername, Username, PlexUsername, Email) ?? $"User {Id}";

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

internal sealed class SeerrPageInfoDto
{
    public int? Pages { get; set; }
    public int? PageSize { get; set; }
    public int? Results { get; set; }
    public int? Page { get; set; }
}

internal sealed class SeerrUserPageDto
{
    public SeerrPageInfoDto? PageInfo { get; set; }
    public List<SeerrUserDto>? Results { get; set; }
}

internal sealed class SeerrResultPageDto
{
    public int? Page { get; set; }
    public int? TotalPages { get; set; }
    public int? TotalResults { get; set; }
    public List<SeerrResultDto>? Results { get; set; }
}

/// <summary>A search or discovery hit: a movie, a series, a person or a collection, told
/// apart by <see cref="MediaType"/>.</summary>
internal sealed class SeerrResultDto
{
    public int Id { get; set; }
    public string? MediaType { get; set; }
    public string? Title { get; set; }
    public string? Name { get; set; }
    public string? ReleaseDate { get; set; }
    public string? FirstAirDate { get; set; }
    public string? Overview { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public double? VoteAverage { get; set; }
    public int? VoteCount { get; set; }
    public SeerrMediaInfoDto? MediaInfo { get; set; }
}

/// <summary>The request server's own record of a title: where it stands, and — once it is
/// in — which Jellyfin item it is.</summary>
internal sealed class SeerrMediaInfoDto
{
    public int Id { get; set; }
    public int TmdbId { get; set; }
    public int? TvdbId { get; set; }
    public int? Status { get; set; }
    public int? Status4k { get; set; }
    public string? MediaType { get; set; }
    public string? JellyfinMediaId { get; set; }
    public string? JellyfinMediaId4k { get; set; }
    public List<SeerrSeasonStatusDto>? Seasons { get; set; }
    public List<SeerrRequestDto>? Requests { get; set; }
}

internal sealed class SeerrSeasonStatusDto
{
    public int SeasonNumber { get; set; }
    public int? Status { get; set; }
    public int? Status4k { get; set; }
}

internal sealed class SeerrGenreDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

internal sealed class SeerrCastDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Character { get; set; }
    public string? ProfilePath { get; set; }
}

internal sealed class SeerrCreditsDto
{
    public List<SeerrCastDto>? Cast { get; set; }
}

internal sealed class SeerrExternalIdsDto
{
    public int? TvdbId { get; set; }
    public string? ImdbId { get; set; }
}

internal sealed class SeerrMovieDetailsDto
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? ReleaseDate { get; set; }
    public string? Overview { get; set; }
    public string? Tagline { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public int? Runtime { get; set; }
    public double? VoteAverage { get; set; }
    public string? Status { get; set; }
    public List<SeerrGenreDto>? Genres { get; set; }
    public SeerrCreditsDto? Credits { get; set; }
    public SeerrExternalIdsDto? ExternalIds { get; set; }
    public SeerrMediaInfoDto? MediaInfo { get; set; }
}

internal sealed class SeerrSeasonDto
{
    public int SeasonNumber { get; set; }
    public string? Name { get; set; }
    public int? EpisodeCount { get; set; }
    public string? AirDate { get; set; }
}

internal sealed class SeerrTvDetailsDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? FirstAirDate { get; set; }
    public string? Overview { get; set; }
    public string? Tagline { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public List<int>? EpisodeRunTime { get; set; }
    public double? VoteAverage { get; set; }
    public string? Status { get; set; }
    public List<SeerrGenreDto>? Genres { get; set; }
    public SeerrCreditsDto? Credits { get; set; }
    public SeerrExternalIdsDto? ExternalIds { get; set; }
    public List<SeerrSeasonDto>? Seasons { get; set; }
    public SeerrMediaInfoDto? MediaInfo { get; set; }
}

internal sealed class SeerrRequestSeasonDto
{
    public int SeasonNumber { get; set; }
    public int? Status { get; set; }
}

internal sealed class SeerrRequestDto
{
    public int Id { get; set; }
    public int Status { get; set; }
    public string? CreatedAt { get; set; }
    public string? Type { get; set; }
    public bool Is4k { get; set; }
    public SeerrMediaInfoDto? Media { get; set; }
    public SeerrUserDto? RequestedBy { get; set; }
    public List<SeerrRequestSeasonDto>? Seasons { get; set; }
    /// <summary>Set instead of everything else when <c>POST /request</c> answers 202: there
    /// was nothing left to request (every season asked for already).</summary>
    public string? Message { get; set; }
}

internal sealed class SeerrRequestPageDto
{
    public SeerrPageInfoDto? PageInfo { get; set; }
    public List<SeerrRequestDto>? Results { get; set; }
}

internal sealed class SeerrRequestCountDto
{
    public int Total { get; set; }
    public int Pending { get; set; }
    public int Approved { get; set; }
    public int Processing { get; set; }
    public int Available { get; set; }
}

internal sealed class SeerrQuotaPartDto
{
    public int? Days { get; set; }
    public int? Limit { get; set; }
    public int? Used { get; set; }
    public int? Remaining { get; set; }
    public bool? Restricted { get; set; }
}

internal sealed class SeerrQuotaDto
{
    public SeerrQuotaPartDto? Movie { get; set; }
    public SeerrQuotaPartDto? Tv { get; set; }
}

internal sealed class SeerrNewRequestDto
{
    public string MediaType { get; set; } = "movie";
    public int MediaId { get; set; }
    public int? TvdbId { get; set; }
    public List<int>? Seasons { get; set; }
    public bool Is4k { get; set; }
}

internal sealed class SeerrJellyfinAuthDto
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

internal sealed class SeerrQuickConnectCodeDto
{
    public string? Code { get; set; }
    public string? Secret { get; set; }
}

internal sealed class SeerrQuickConnectSecretDto
{
    public string Secret { get; set; } = "";
}

internal sealed class SeerrErrorDto
{
    public string? Message { get; set; }
    public string? Error { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(SeerrStatusDto))]
[JsonSerializable(typeof(SeerrPublicSettingsDto))]
[JsonSerializable(typeof(SeerrUserDto))]
[JsonSerializable(typeof(SeerrUserPageDto))]
[JsonSerializable(typeof(SeerrResultPageDto))]
[JsonSerializable(typeof(SeerrMovieDetailsDto))]
[JsonSerializable(typeof(SeerrTvDetailsDto))]
[JsonSerializable(typeof(SeerrRequestDto))]
[JsonSerializable(typeof(SeerrRequestPageDto))]
[JsonSerializable(typeof(SeerrRequestCountDto))]
[JsonSerializable(typeof(SeerrQuotaDto))]
[JsonSerializable(typeof(SeerrNewRequestDto))]
[JsonSerializable(typeof(SeerrJellyfinAuthDto))]
[JsonSerializable(typeof(SeerrQuickConnectCodeDto))]
[JsonSerializable(typeof(SeerrQuickConnectSecretDto))]
[JsonSerializable(typeof(SeerrErrorDto))]
internal partial class SeerrJson : JsonSerializerContext;
