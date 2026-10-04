using System.Text;

// What the program under the pseudoterminal does, by its first argument:
//   args A B …   prints each argument as [A] on a line of its own, then exits 0
//   exit N       prints "bye", exits with N
//   size         prints READY, then "SIZE <columns> <rows>" for every line it reads
//   echo         prints READY, counts the lines it reads until one is END, prints the count
//   flood N      writes N MiB of 80-column lines, then FLOOD-DONE
//   wait         prints READY and reads until its input ends
//   events       (Windows) prints READY, then each console input record it reads: "SIZE c r INFO c r"
//                for a resize, "KEY vk scan char state" for a key press; ends at q
Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
var output = Console.Out;

switch (args.FirstOrDefault())
{
    case "args":
        foreach (var arg in args.Skip(1)) output.Write($"[{arg}]\n");
        output.Flush();
        return 0;

    case "exit":
        output.Write("bye\n");
        output.Flush();
        return int.Parse(args[1]);

    case "size":
        output.Write("READY\n");
        output.Flush();
        while (Console.In.ReadLine() is not null)
        {
            output.Write($"SIZE {Console.WindowWidth} {Console.WindowHeight}\n");
            output.Flush();
        }
        return 0;

    case "echo":
    {
        output.Write("READY\n");
        output.Flush();
        int lines = 0;
        string? line;
        while ((line = Console.In.ReadLine()) is not null)
        {
            if (line == "END") break;
            lines++;
        }
        output.Write($"GOT END after {lines} lines\n");
        output.Flush();
        return 0;
    }

    case "flood":
    {
        var block = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(
            "the quick brown fox jumps over the lazy dog 0123456789 ABCDEFGHIJKLMNOPQRSTU\r\n", 800)));
        using var stdout = Console.OpenStandardOutput();
        long total = long.Parse(args[1]) << 20;
        for (long written = 0; written < total; written += block.Length) stdout.Write(block);
        stdout.Write("FLOOD-DONE\r\n"u8);
        return 0;
    }

    case "wait":
        output.Write("READY\n");
        output.Flush();
        while (Console.In.ReadLine() is not null) { }
        return 0;

    case "events" when OperatingSystem.IsWindows():
        return ConsoleEvents.Run(output);

    default:
        Console.Error.WriteLine("usage: args|exit|size|echo|flood|wait|events");
        return 2;
}
