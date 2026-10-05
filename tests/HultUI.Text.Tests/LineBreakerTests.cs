using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// LineBreaker keeps one ICU iterator per thread and points it at each text in turn, where it
/// used to open a fresh one per call. The breaks must be the ones a fresh iterator finds —
/// whatever came before on that thread, and with other threads breaking text at the same time.
/// </summary>
public class LineBreakerTests
{
    private static readonly string[] Texts =
    [
        "pretty red table chair house keyboard pretty red table chair house keyboard pretty red table",
        "a",
        "",
        "well-known x-ray https://example.com/a/b/c.txt",
        "first line\nsecond line\n\nfourth",
        "日本語のテキストを表示する",
        "commit שלום merged by אבג into main",
        "supercalifragilisticexpialidocious",
        "   ",
        "short",
    ];

    // What a fresh iterator finds: each text broken on a thread of its own, whose first call
    // opens one.
    private static List<int>[] Fresh() => Texts.Select(text =>
    {
        List<int>? breaks = null;
        var thread = new Thread(() => breaks = LineBreaker.FindBreakOpportunities(text));
        thread.Start();
        thread.Join();
        return breaks!;
    }).ToArray();

    [Fact]
    public void AReusedIterator_FindsWhatAFreshOneFinds_WhateverCameBefore()
    {
        var fresh = Fresh();
        Assert.Contains(fresh, b => b.Count > 5);   // the texts really have breaks to find

        // One thread, every text, forwards then backwards: short after long, empty in between.
        var order = Enumerable.Range(0, Texts.Length).Concat(Enumerable.Range(0, Texts.Length).Reverse());
        foreach (int i in order)
            Assert.Equal(fresh[i], LineBreaker.FindBreakOpportunities(Texts[i]));
    }

    [Fact]
    public void ThreadsBreakingAtOnce_EachGetTheirOwnTextsBreaks()
    {
        var fresh = Fresh();
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        var threads = Enumerable.Range(0, 8).Select(t => new Thread(() =>
        {
            for (int n = 0; n < 300; n++)
            {
                int i = (t * 7 + n) % Texts.Length;
                if (!LineBreaker.FindBreakOpportunities(Texts[i]).SequenceEqual(fresh[i]))
                    failures.Add($"thread {t}, call {n}: \"{Texts[i]}\"");
            }
        })).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        Assert.Empty(failures);
    }
}
