using XtermNet;

if (args.Length == 0)
{
    Console.WriteLine("usage: XtermNet <q1|q2|...|q8> [options]   (child modes: echo, flood <MiB>)");
    return 2;
}

return args[0] switch
{
    "q1" => Q1.Run(),
    "q2" => Q2.Run(),
    "q3" => Q3.Run(),
    "q3-detail" => Q3.Detail(),
    "q4" => Q4.Run(args.Skip(1).ToArray()),
    "q5" => Q5.Run(),
    "q6" => Q6.Run(),
    "q7" => Q7.Run(args.Skip(1).ToArray()),
    "q8" => Q8.Run(args.Skip(1).ToArray()),
    // Children the ConPTY questions run under the pseudoconsole: this same executable, so the
    // spike needs nothing installed beyond what Windows and Git already put there.
    "echo" => Children.Echo(),
    "flood" => Children.Flood(args.Length > 1 ? int.Parse(args[1]) : 64),
    _ => Fail($"unknown probe {args[0]}"),
};

static int Fail(string m) { Console.Error.WriteLine(m); return 2; }
