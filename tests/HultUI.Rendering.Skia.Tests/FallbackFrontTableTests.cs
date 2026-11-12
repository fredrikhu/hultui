using System.Globalization;
using HultUI.Core.Styling;
using HultUI.Text;
using SkiaSharp;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// The table in front of <see cref="SkiaFontCollection.MatchCharacter(int, FontWeight, FontSlant, string?)"/>'s
/// keyed lookup: a repeat is answered from it, before the script's language is worked out; a question that
/// differs in any field the answer depends on (code point, weight, slant, the paragraph's Han context, the UI
/// culture) is not; <see cref="SkiaFontCollection.Register(SKTypeface)"/> empties it; and none of it allocates
/// once warm. <see cref="SkiaFontCollection.KeyedLookups"/> counts the questions the table did not answer.
/// </summary>
public class FallbackFrontTableTests
{
    private const int East = 0x6771;            // 東, in every CJK language's faces
    private const int SameSlot = East + 4096;   // 睱 (U+7771): the slot East has

    // What a face answer is, comparable across collections (each makes its own wrappers).
    private static string Face(IResolvedFont? font) =>
        font?.NativeHandle is SKTypeface t ? $"{t.FamilyName} {t.FontWeight} {t.FontSlant}" : "none";

    private static string FreshFace(int codepoint, FontWeight weight, FontSlant slant, string? hanContext) =>
        Face(new SkiaFontCollection().MatchCharacter(codepoint, weight, slant, hanContext));

    [Fact]
    public void ARepeat_IsAnsweredByTheTable_WithoutTheKeyedLookup()
    {
        var fonts = new SkiaFontCollection();
        var first = fonts.MatchCharacter(East, FontWeight.Regular, FontSlant.Normal, "ja");
        Assert.Equal(1, fonts.KeyedLookups);

        for (int i = 0; i < 100; i++)
            Assert.Same(first, fonts.MatchCharacter(East, FontWeight.Regular, FontSlant.Normal, "ja"));
        Assert.Equal(1, fonts.KeyedLookups);
    }

    [Fact]
    public void EveryFieldTheAnswerDependsOn_AsksAgain_AndGetsItsOwnAnswer()
    {
        var fonts = new SkiaFontCollection();
        var saved = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            int asked = 0;
            void Ask(int codepoint, FontWeight weight, FontSlant slant, string? hanContext)
            {
                var answer = fonts.MatchCharacter(codepoint, weight, slant, hanContext);
                Assert.Equal(++asked, fonts.KeyedLookups);
                Assert.Equal(FreshFace(codepoint, weight, slant, hanContext), Face(answer));
            }

            Ask(East, FontWeight.Regular, FontSlant.Normal, null);
            Ask(East, FontWeight.Bold, FontSlant.Normal, null);       // weight
            Ask(East, FontWeight.Bold, FontSlant.Italic, null);       // slant
            Ask(East, FontWeight.Bold, FontSlant.Italic, "ja");       // the paragraph's Han context
            Ask(East, FontWeight.Bold, FontSlant.Italic, "ko");
            CultureInfo.CurrentUICulture = new CultureInfo("zh-TW"); // the UI culture
            Ask(East, FontWeight.Bold, FontSlant.Italic, "ko");
            Ask(SameSlot, FontWeight.Bold, FontSlant.Italic, "ko");   // another code point in the same slot
            Ask(East, FontWeight.Bold, FontSlant.Italic, "ko");       // ... which took the slot over
        }
        finally
        {
            CultureInfo.CurrentUICulture = saved;
        }
    }

    [HanFontsFact]
    public void TheHanContext_AndTheCulture_AreNotServedEachOthersFaces()
    {
        // The faces differ on any desktop: Yu Gothic / Malgun Gothic / Microsoft JhengHei on Windows,
        // Noto Sans CJK JP / KR / TC on Linux. The count test above holds the table to asking; this one
        // holds what a table that did not ask would have drawn.
        var fonts = new SkiaFontCollection();
        var saved = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            var japanese = fonts.MatchCharacter(East, FontWeight.Regular, FontSlant.Normal, "ja");
            var korean = fonts.MatchCharacter(East, FontWeight.Regular, FontSlant.Normal, "ko");
            Assert.NotEqual(Face(japanese), Face(korean));
            Assert.Equal(FreshFace(East, FontWeight.Regular, FontSlant.Normal, "ko"), Face(korean));

            var simplified = fonts.MatchCharacter(East, FontWeight.Regular, FontSlant.Normal, null);
            CultureInfo.CurrentUICulture = new CultureInfo("zh-TW");
            var traditional = fonts.MatchCharacter(East, FontWeight.Regular, FontSlant.Normal, null);
            Assert.NotEqual(Face(simplified), Face(traditional));
            Assert.Equal(FreshFace(East, FontWeight.Regular, FontSlant.Normal, null), Face(traditional));
        }
        finally
        {
            CultureInfo.CurrentUICulture = saved;
        }
    }

    [Fact]
    public void Register_EmptiesTheTable()
    {
        var fonts = new SkiaFontCollection();
        fonts.MatchCharacter(East, FontWeight.Regular, FontSlant.Normal, "ja");
        fonts.MatchCharacter(East, FontWeight.Regular, FontSlant.Normal, "ja");
        Assert.Equal(1, fonts.KeyedLookups);

        fonts.Register(SKTypeface.Default);
        fonts.MatchCharacter(East, FontWeight.Regular, FontSlant.Normal, "ja");
        Assert.Equal(2, fonts.KeyedLookups);
    }

    [Fact]
    public void TheTable_IsMadeAtTheFirstQuestion()
    {
        var fonts = new SkiaFontCollection();
        fonts.Resolve("sans-serif", FontWeight.Regular, FontSlant.Normal);
        Assert.False(fonts.HasFrontTable);

        fonts.MatchCharacter(East, FontWeight.Regular, FontSlant.Normal);
        Assert.True(fonts.HasFrontTable);

        Assert.False(SkiaFontCollection.Pinned(SKTypeface.Default).HasFrontTable);
        var pinned = SkiaFontCollection.Pinned(SKTypeface.Default);
        Assert.Null(pinned.MatchCharacter(East, FontWeight.Regular, FontSlant.Normal));
        Assert.False(pinned.HasFrontTable);
    }

    [Fact]
    public void Lookups_AllocateNothing_OnceWarm_HitOrSlotTakenOver()
    {
        var fonts = new SkiaFontCollection();
        void Pass()
        {
            for (int c = 0x4E00; c < 0x4E00 + 2000; c++)
                fonts.MatchCharacter(c, FontWeight.Regular, FontSlant.Normal, "ja");
            // Two code points in one slot, taking it from each other: answered by the keyed lookup.
            for (int i = 0; i < 200; i++)
            {
                fonts.MatchCharacter(East, FontWeight.Regular, FontSlant.Normal, "ja");
                fonts.MatchCharacter(SameSlot, FontWeight.Regular, FontSlant.Normal, "ja");
            }
        }

        Pass();
        Pass();
        int keyed = fonts.KeyedLookups;
        long before = GC.GetAllocatedBytesForCurrentThread();
        Pass();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, bytes);
        Assert.Equal(keyed + 400, fonts.KeyedLookups);
    }
}
