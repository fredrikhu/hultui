using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Covers the two halves of elevated file operations that can be exercised without
/// actually raising a UAC prompt: the worker (given a job file, it performs the batch
/// and writes a result file — run here in-process, unelevated, which is exactly what the
/// elevated child does with more rights) and the access-denied tagging that decides when
/// the app offers elevation at all.
/// </summary>
public class ElevationTests
{
    [Fact]
    public void Worker_Copy_CopiesFileAndReportsSuccess()
    {
        using var t = new Tree();
        var src = t.File("a.txt", "hello");
        var dest = Path.Combine(t.Out, "a.txt");

        var result = t.RunJob(new ElevatedJob
        {
            Verb = "copy",
            Items = [new ElevatedItem { Source = src, Target = dest }],
        });

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal("hello", File.ReadAllText(dest));
    }

    [Fact]
    public void Worker_Move_MovesFile()
    {
        using var t = new Tree();
        var src = t.File("b.txt", "x");
        var dest = Path.Combine(t.Out, "b.txt");

        var result = t.RunJob(new ElevatedJob
        {
            Verb = "move",
            Items = [new ElevatedItem { Source = src, Target = dest }],
        });

        Assert.Equal(1, result.Succeeded);
        Assert.False(File.Exists(src));
        Assert.True(File.Exists(dest));
    }

    [Fact]
    public void Worker_DeleteMkdirRename_AllApply()
    {
        using var t = new Tree();
        var doomed = t.File("gone.txt", "x");
        var newDir = Path.Combine(t.Root, "made");
        var toRename = t.File("old.txt", "x");
        var renamed = Path.Combine(t.Root, "new.txt");

        Assert.Equal(1, t.RunJob(new ElevatedJob
        {
            Verb = "delete", Recycle = false,
            Items = [new ElevatedItem { Source = doomed }],
        }).Succeeded);
        Assert.False(File.Exists(doomed));

        Assert.Equal(1, t.RunJob(new ElevatedJob
        {
            Verb = "mkdir",
            Items = [new ElevatedItem { Source = newDir, Target = newDir, IsDir = true }],
        }).Succeeded);
        Assert.True(Directory.Exists(newDir));

        Assert.Equal(1, t.RunJob(new ElevatedJob
        {
            Verb = "rename",
            Items = [new ElevatedItem { Source = toRename, Target = renamed }],
        }).Succeeded);
        Assert.True(File.Exists(renamed));
    }

    [Fact]
    public void Worker_ReportsPerItemFailureWithoutAbortingTheBatch()
    {
        using var t = new Tree();
        var good = t.File("good.txt", "x");
        var missing = Path.Combine(t.Root, "missing.txt");

        var result = t.RunJob(new ElevatedJob
        {
            Verb = "copy",
            Items =
            [
                new ElevatedItem { Source = missing, Target = Path.Combine(t.Out, "missing.txt") },
                new ElevatedItem { Source = good, Target = Path.Combine(t.Out, "good.txt") },
            ],
        });

        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Succeeded);       // the second item still ran
        Assert.NotNull(result.Errors[0]);
        Assert.Null(result.Errors[1]);
        Assert.True(File.Exists(Path.Combine(t.Out, "good.txt")));
    }

    [Fact]
    public void Worker_UnknownVerb_IsReportedNotThrown()
    {
        using var t = new Tree();
        var result = t.RunJob(new ElevatedJob
        {
            Verb = "format-c",
            Items = [new ElevatedItem { Source = t.File("x.txt", "x") }],
        });

        Assert.Equal(1, result.Failed);
        Assert.Contains("unknown operation", result.Errors[0]!);
    }

    [Fact]
    public void Worker_MissingJobFile_ExitsNonZero()
        => Assert.Equal(2, Elevation.RunWorker(Path.Combine(Path.GetTempPath(), "no-such-job.json")));

    [Fact]
    public void PermissionFailure_IsTaggedAndCleanedForDisplay()
    {
        using var t = new Tree();
        var dir = Path.Combine(t.Root, "adir");
        Directory.CreateDirectory(dir);

        // Deleting a directory through the file path raises UnauthorizedAccessException on
        // every platform — a real permission-class failure, no ACL fixture needed.
        var err = FileOps.DeletePermanent(dir, isDir: false);

        Assert.NotNull(err);
        Assert.True(FileOps.IsDenied(err));
        var shown = FileOps.Clean(err)!;
        Assert.DoesNotContain("[denied]", shown);       // the marker never reaches the UI
        Assert.NotEmpty(shown);
    }

    [Fact]
    public void OrdinaryFailure_IsNotTaggedAsDenied()
    {
        using var t = new Tree();
        var err = FileOps.DeletePermanent(Path.Combine(t.Root, "nope", "deep"), isDir: true);

        Assert.False(FileOps.IsDenied(err));
        Assert.Equal(err, FileOps.Clean(err));          // Clean is a no-op on untagged errors
    }

    /// <summary>A temp tree plus a job-file round trip through <see cref="Elevation.RunWorker"/>.</summary>
    private sealed class Tree : System.IDisposable
    {
        public readonly string Root, Out;

        public Tree()
        {
            Root = Path.Combine(Path.GetTempPath(), "vesper-elev-" + System.Guid.NewGuid().ToString("N"));
            Out = Path.Combine(Root, "out");
            Directory.CreateDirectory(Out);
        }

        public string File(string name, string content)
        {
            var path = Path.Combine(Root, name);
            System.IO.File.WriteAllText(path, content);
            return path;
        }

        /// <summary>Serialize the job the way the app does, run the worker, read the result
        /// file back — the same handshake the elevated child performs.</summary>
        public ElevatedResult RunJob(ElevatedJob job)
        {
            var jobPath = Path.Combine(Root, System.Guid.NewGuid().ToString("N") + ".job.json");
            System.IO.File.WriteAllText(jobPath, JsonSerializer.Serialize(job, JobOptions));
            Elevation.RunWorker(jobPath);
            Assert.True(System.IO.File.Exists(jobPath + ".result"), "worker wrote no result file");
            return JsonSerializer.Deserialize<ElevatedResult>(
                System.IO.File.ReadAllText(jobPath + ".result"), JobOptions)!;
        }

        private static readonly JsonSerializerOptions JobOptions = new();

        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch { /* best-effort temp cleanup */ }
        }
    }
}
