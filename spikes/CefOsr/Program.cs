using System.Runtime.InteropServices;
using CefOsr;
using CefOsr.Capi;
using static CefOsr.Native;

// A Chromium subprocess started from this executable (no browser_subprocess_path): the
// handshake and cef_execute_process are all it does. CEF does not pass our own options on,
// so the library's directory comes through the environment, which children inherit.
if (args.Any(a => a.StartsWith("--type=")))
    return Host.ExecuteProcess(args, Environment.GetEnvironmentVariable("HULTUI_CEF_DIR") ?? "/usr/lib64/cef");

if (args.Length == 0)
{
    Console.WriteLine("usage: CefOsr <q1|q2|...> [--cef-dir=] [--ozone=] [--sandbox=off] [--subprocess=] [--loop=work|external|mt] ...");
    return 2;
}

var probe = args[0];
var opt = Options.Parse(args.Skip(1));
// Children inherit the native environment, not .NET's copy of it.
setenv("HULTUI_CEF_DIR", opt.CefDir, 1);

return probe switch
{
    "q1" => Q1.Run(opt),
    "q3" => Q3.Run(opt),
    "q4" => Q4.Run(opt),
    "q5" => Q5.Run(opt),
    "q6" => Q6.Run(opt),
    "q7" => Q7.Run(opt),
    "q8" => Q8.Run(opt),
    "q9" => Q9.Run(opt),
    "q10" => Q10.Run(opt),
    "lifetime" => Lifetime.Run(opt),
    _ => Fail($"unknown probe {probe}"),
};

static int Fail(string m) { Console.Error.WriteLine(m); return 2; }

[DllImport("libc")] static extern int setenv(string name, string value, int overwrite);
