using HultUI.Core.Rendering;
using HultUI.Core.Threading;
using HultUI.Rendering.Skia;
using SkiaSharp;

namespace HultUI;

public sealed partial class Window
{
    /// <summary>Frames asked for by <see cref="SaveFrame"/>, written by the next frame drawn.
    /// UI thread only.</summary>
    private readonly List<(string Path, TaskCompletionSource Done)> _frameSaves = [];

    /// <summary>Saves asked for from another thread, on their way to the UI thread: failed by
    /// <see cref="Dispose"/> when the window goes before they arrive (a disposed application
    /// drops what is posted to it). Under its own lock.</summary>
    private readonly List<TaskCompletionSource> _frameSavesPosted = [];

    /// <summary>
    /// Writes the window's next complete frame to <paramref name="path"/> as a PNG, at device
    /// pixels, read from the surface HultUI draws into rather than from the screen: whole whatever
    /// covers the window, and never half drawn or scaled the way a screen grab or
    /// <c>PrintWindow</c> can be just after a maximize on a scaled monitor.
    ///
    /// <para>Any thread may call it. It asks the window for a frame, so one comes even when nothing
    /// changed; every frame is drawn whole, so that frame is what the window shows. The task
    /// completes once the file is written (encoding happens off the UI thread), and fails with
    /// what went wrong, or when the window closes first. A minimized window is not drawn, and
    /// its frame is written on the restore.</para>
    ///
    /// <para>For tests and tools: it waits for the GPU and copies the frame. Costs nothing per
    /// frame while nothing is asked. <c>HULTUI_FRAME_REQUESTS</c> asks for one from outside the
    /// process (<see cref="FrameRequests"/>).</para>
    /// </summary>
    public Task SaveFrame(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        path = Path.GetFullPath(path);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (UIThread.IsCurrent) QueueFrameSave(path, done);
        else
        {
            lock (_frameSavesPosted)
            {
                if (_disposed || Application.IsDisposed)
                {
                    done.TrySetException(new ObjectDisposedException(nameof(Window)));
                    return done.Task;
                }
                _frameSavesPosted.Add(done);
            }
            // To this window's application, not whichever is current (headless sessions side by side).
            Application.Post(() =>
            {
                lock (_frameSavesPosted) _frameSavesPosted.Remove(done);
                QueueFrameSave(path, done);
            });
        }
        return done.Task;
    }

    private void QueueFrameSave(string path, TaskCompletionSource done)
    {
        if (_disposed)
        {
            done.TrySetException(new ObjectDisposedException(nameof(Window)));
            return;
        }
        _frameSaves.Add((path, done));
        _root?.MarkPaintDirty();
        UIThread.WakeIfParked();
    }

    /// <summary>
    /// Presents this frame and writes it for every <see cref="SaveFrame"/> waiting. In place of
    /// <see cref="IRenderSurface.Present"/>, only on a frame something asked for.
    /// </summary>
    private void PresentSavingFrame()
    {
        var saves = _frameSaves.ToArray();
        _frameSaves.Clear();
        SKImage frame;
        try
        {
            if (PlatformWindow.Surface is ISkiaFrameCapture capture)
                frame = capture.PresentCapturing();
            else
            {
                PlatformWindow.Surface.Present();
                throw new NotSupportedException("This window's surface cannot hand over its frame.");
            }
        }
        catch (Exception ex)
        {
            foreach (var save in saves) save.Done.TrySetException(ex);
            return;
        }

        // Off the UI thread: a 4K PNG takes long enough to be a dropped frame.
        _ = Task.Run(() =>
        {
            try
            {
                using (frame)
                using (var png = frame.Encode(SKEncodedImageFormat.Png, 100)
                    ?? throw new InvalidOperationException("The frame could not be encoded as PNG."))
                {
                    foreach (var (path, done) in saves)
                    {
                        try
                        {
                            WriteWhole(path, png);
                            done.TrySetResult();
                        }
                        catch (Exception ex)
                        {
                            done.TrySetException(ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                foreach (var save in saves) save.Done.TrySetException(ex);
            }
        });
    }

    /// <summary>Written beside and moved into place, so a tool that waits for the file never
    /// reads half of one.</summary>
    private static void WriteWhole(string path, SKData png)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string partial = path + ".partial";
        using (var file = File.Create(partial))
            png.SaveTo(file);
        File.Move(partial, path, overwrite: true);
    }

    /// <summary>What <see cref="SaveFrame"/> still waited for when the window went.</summary>
    private void FailFrameSaves()
    {
        foreach (var save in _frameSaves)
            save.Done.TrySetException(new ObjectDisposedException(nameof(Window)));
        _frameSaves.Clear();
        lock (_frameSavesPosted)
        {
            foreach (var done in _frameSavesPosted)
                done.TrySetException(new ObjectDisposedException(nameof(Window)));
            _frameSavesPosted.Clear();
        }
    }
}
