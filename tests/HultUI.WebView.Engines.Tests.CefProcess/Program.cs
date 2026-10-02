using HultUI.WebView.Cef;

// Starts CEF and returns from Main, so the test can watch the process exit. Prints what it
// sees for the test to check; exit code 3 means there is no CEF here.
var engine = new CefEngine();
if (!engine.Availability.IsAvailable)
{
    Console.WriteLine("unavailable: " + engine.Availability.Reason);
    return 3;
}

CefRuntime.ShuttingDown += () => Console.WriteLine("process exit: shutting down");
CefRuntime.ShutDown += took => Console.WriteLine($"shut down in {took.TotalMilliseconds:F0} ms on '{Thread.CurrentThread.Name}'");

var error = CefRuntime.Start(windowSystem: null).Wait(TimeSpan.FromSeconds(20)) ? CefRuntime.Error : "timed out";
if (error is not null)
{
    Console.WriteLine("did not start: " + error);
    return 1;
}
Console.WriteLine("started: " + CefRuntime.Description);
Console.WriteLine("profile: " + CefRuntime.Profile);

if (args.Contains("--second-engine"))
{
    engine.Dispose();
    using var second = new CefEngine();
    Console.WriteLine($"a second engine after disposing the first: {second.Availability.IsAvailable}");
}
var hold = Array.IndexOf(args, "--hold");
if (hold >= 0) Thread.Sleep(int.Parse(args[hold + 1]));
Console.Out.Flush();
return 0;
