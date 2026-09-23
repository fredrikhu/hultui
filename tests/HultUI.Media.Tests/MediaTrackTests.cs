using HultUI.Media;
using Xunit;

namespace HultUI.Media.Tests;

/// <summary>
/// What a track is called. The case this exists for is a dubbed release: two audio tracks
/// muxed with the same embedded title ("Stereo"), differing only in a language code — a
/// menu naming tracks by title alone offers the same word twice.
/// </summary>
public class MediaTrackTests
{
    [Fact]
    public void TwoTracksTitledTheSameAreToldApartByLanguage()
    {
        Assert.Equal("English · Stereo", MediaTrack.Describe("Stereo", "eng", 1));
        Assert.Equal("Japanese · Stereo", MediaTrack.Describe("Stereo", "jpn", 2));
    }

    [Fact]
    public void EitherHalfOnItsOwnIsEnough()
    {
        Assert.Equal("Japanese", MediaTrack.Describe(null, "jpn", 1));
        Assert.Equal("Commentary", MediaTrack.Describe("Commentary", null, 1));
        Assert.Equal("Japanese", MediaTrack.Describe("   ", "jpn", 1));
    }

    [Fact]
    public void AFileThatSaysNothingLeavesTheTrackNumber()
    {
        Assert.Equal("Track 3", MediaTrack.Describe(null, null, 3));
        // "und" is what a muxer writes for "no language recorded".
        Assert.Equal("Track 3", MediaTrack.Describe(null, "und", 3));
    }

    [Fact]
    public void ATitleThatRepeatsTheLanguageIsNotSaidTwice()
    {
        Assert.Equal("English", MediaTrack.Describe("English", "eng", 1));
        Assert.Equal("English", MediaTrack.Describe("eng", "eng", 1));
    }

    [Fact]
    public void BibliographicCodesAreUnderstoodToo()
    {
        // What a Matroska file usually carries; the runtime only knows the /T forms.
        Assert.Equal("German", MediaTrack.LanguageName("ger"));
        Assert.Equal("German", MediaTrack.LanguageName("deu"));
        Assert.Equal("French", MediaTrack.LanguageName("fre"));
        Assert.Equal("Japanese", MediaTrack.LanguageName("ja"));
    }

    [Fact]
    public void ACodeNobodyKnowsLeavesTheTitleAlone()
    {
        Assert.Null(MediaTrack.LanguageName("qzz"));
        Assert.Equal("Stereo", MediaTrack.Describe("Stereo", "qzz", 1));
    }
}
