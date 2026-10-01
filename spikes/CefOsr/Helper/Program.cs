using System.Runtime.InteropServices;
using CefOsr.Capi;

// The subprocess executable named in browser_subprocess_path: what question 1 weighs
// against re-running the app. The handshake, then cef_execute_process, which runs the
// zygote, renderer, GPU or utility process and returns its exit code.
// CEFOSR_THREADS=1 prints how many threads the process has when it gets here, because
// Chromium's zygote has to be single-threaded to sandbox itself.
unsafe
{
    if (Environment.GetEnvironmentVariable("CEFOSR_THREADS") == "1")
        Console.Error.WriteLine($"helper {Environment.ProcessId}: threads at Main: " + string.Join(", ", Directory.GetDirectories("/proc/self/task").Select(t => File.ReadAllText(t + "/comm").Trim())));
    var dir = Environment.GetEnvironmentVariable("HULTUI_CEF_DIR") ?? "/usr/lib64/cef";
    Cef.Load(NativeLibrary.Load(Path.Combine(dir, "libcef.so")));
    Cef.cef_api_hash(CefApi.Version, 0);
    var all = new[] { Environment.ProcessPath ?? "helper" }.Concat(args).ToArray();
    var argv = (byte**)NativeMemory.AllocZeroed((nuint)(sizeof(byte*) * (all.Length + 1)));
    for (int i = 0; i < all.Length; i++) argv[i] = (byte*)Marshal.StringToCoTaskMemUTF8(all[i]);
    var mainArgs = new cef_main_args_t { argc = all.Length, argv = argv };
    return Cef.cef_execute_process(&mainArgs, null, null);
}
