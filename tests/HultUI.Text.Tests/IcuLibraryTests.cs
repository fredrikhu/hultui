using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using HultUI.Text;
using Xunit;

namespace HultUI.Text.Tests;

/// <summary>Runs alone: it unloads ICU's bindings under every other test's feet.</summary>
[CollectionDefinition(nameof(IcuLoading), DisableParallelization = true)]
public sealed class IcuLoading;

/// <summary>
/// Loading ICU from several threads at once. The loader wrote each attempt straight into the
/// shared handle, and on Linux the first attempt always fails, so a thread still loading zeroed
/// the handle after another had finished and said so; a third then looked a function up in no
/// library and threw "Value cannot be null (Parameter 'handle')". Text is laid out on several
/// threads at once, and CI's Linux job failed on it (FallbackSequenceTests, 2026-10-05).
/// </summary>
[Collection(nameof(IcuLoading))]
public class IcuLibraryTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr ErrorName(int code);

    [Fact]
    public void ThreadsLoadingItAtOnce_AllFindItsFunctions()
    {
        const int threads = 8;
        var errors = new ConcurrentQueue<Exception>();
        for (int round = 0; round < 200 && errors.IsEmpty; round++)
        {
            IcuLibrary.Reset();
            using var start = new Barrier(threads);
            var workers = Enumerable.Range(0, threads).Select(_ => new Thread(() =>
            {
                start.SignalAndWait();
                try { IcuLibrary.GetDelegate<ErrorName>("u_errorName"); }
                catch (Exception e) { errors.Enqueue(e); }
            })).ToList();
            workers.ForEach(t => t.Start());
            workers.ForEach(t => t.Join());
        }
        Assert.Empty(errors);
    }
}
