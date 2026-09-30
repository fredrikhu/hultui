namespace Aperture.Core.Requests;

/// <summary>A film studio (<see cref="DiscoverKind.Movie"/>) or a television network
/// (<see cref="DiscoverKind.Tv"/>) to browse by, with the TMDB logo to draw its card with.</summary>
public sealed record DiscoverCompany(int Id, string Name, DiscoverKind Kind, string? LogoPath)
{
    /// <summary><c>movie:2</c> for a studio, <c>tv:213</c> for a network — the route id of its page.</summary>
    public string Key => $"{(Kind == DiscoverKind.Movie ? "movie" : "tv")}:{Id}";
}

/// <summary>
/// The studios and networks Discover's "Studios" and "Networks" rows offer. The request
/// server's API does not serve these: its own web page draws both rows from lists compiled
/// into it (<c>src/components/Discover/StudioSlider</c> and <c>NetworkSlider</c> in Seerr),
/// so these are the same lists, in the same order, with the same TMDB ids and logos — a
/// household that uses both sees the same row in both.
/// </summary>
public static class DiscoverCompanies
{
    public static readonly IReadOnlyList<DiscoverCompany> Studios =
    [
        Studio(2, "Disney", "/wdrCwmRnLFJhEoH8GSfymY85KHT.png"),
        Studio(127928, "20th Century Studios", "/h0rjX5vjW5r8yEnUBStFarjcLT4.png"),
        Studio(34, "Sony Pictures", "/GagSvqWlyPdkFHMfQ3pNq6ix9P.png"),
        Studio(174, "Warner Bros. Pictures", "/ky0xOc5OrhzkZ1N6KyUxacfQsCk.png"),
        Studio(33, "Universal", "/8lvHyhjr8oUKOOy2dKXoALWKdp0.png"),
        Studio(4, "Paramount", "/fycMZt242LVjagMByZOLUGbCvv3.png"),
        Studio(3, "Pixar", "/1TjvGVDMYsj6JBxOAkUHpPEwLf7.png"),
        Studio(521, "Dreamworks", "/kP7t6RwGz2AvvTkvnI1uteEwHet.png"),
        Studio(420, "Marvel Studios", "/hUzeosd33nzE5MCNsZxCGEKTXaQ.png"),
        Studio(9993, "DC", "/2Tc1P3Ac8M479naPp1kYT3izLS5.png"),
        Studio(41077, "A24", "/1ZXsGaFPgrgS6ZZGS37AqD5uU12.png"),
    ];

    public static readonly IReadOnlyList<DiscoverCompany> Networks =
    [
        Network(213, "Netflix", "/wwemzKWzjKYJFfCeiB57q3r4Bcm.png"),
        Network(2739, "Disney+", "/gJ8VX6JSu3ciXHuC2dDGAo2lvwM.png"),
        Network(1024, "Prime Video", "/ifhbNuuVnlwYy5oXA5VIb2YR8AZ.png"),
        Network(2552, "Apple TV+", "/4KAy34EHvRM25Ih8wb82AuGU7zJ.png"),
        Network(453, "Hulu", "/pqUTCleNUiTLAVlelGxUgWn1ELh.png"),
        Network(49, "HBO", "/tuomPhY2UtuPTqqFnKMVHvSb724.png"),
        Network(4353, "Discovery+", "/1D1bS3Dyw4ScYnFWTlBOvJXC3nb.png"),
        Network(2, "ABC", "/ndAvF4JLsliGreX87jAc9GdjmJY.png"),
        Network(19, "FOX", "/1DSpHrWyOORkL9N2QHX7Adt31mQ.png"),
        Network(359, "Cinemax", "/6mSHSquNpfLgDdv6VnOOvC5Uz2h.png"),
        Network(174, "AMC", "/pmvRmATOCaDykE6JrVoeYxlFHw3.png"),
        Network(67, "Showtime", "/Allse9kbjiP6ExaQrnSpIhkurEi.png"),
        Network(318, "Starz", "/8GJjw3HHsAJYwIWKIPBPfqMxlEa.png"),
        Network(71, "The CW", "/ge9hzeaU7nMtQ4PjkFlc68dGAJ9.png"),
        Network(6, "NBC", "/o3OedEP0f9mfZr33jz2BfXOUK5.png"),
        Network(16, "CBS", "/nm8d7P7MJNiBLdgIzUK0gkuEA4r.png"),
        Network(4330, "Paramount+", "/fi83B1oztoS47xxcemFdPMhIzK.png"),
        Network(4, "BBC One", "/mVn7xESaTNmjBUyUtGNvDQd3CT1.png"),
        Network(56, "Cartoon Network", "/c5OC6oVCg6QP4eqzW6XIq17CQjI.png"),
        Network(80, "Adult Swim", "/9AKyspxVzywuaMuZ1Bvilu8sXly.png"),
        Network(13, "Nickelodeon", "/ikZXxg6GnwpzqiZbRPhJGaZapqB.png"),
        Network(3353, "Peacock", "/gIAcGTjKKr0KOHL5s4O36roJ8p7.png"),
    ];

    private static DiscoverCompany Studio(int id, string name, string logo) => new(id, name, DiscoverKind.Movie, logo);
    private static DiscoverCompany Network(int id, string name, string logo) => new(id, name, DiscoverKind.Tv, logo);
}
