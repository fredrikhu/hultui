// The runner (`meridian run <collection> --env <name>`) lands in P7, on top of the
// P6 run engine. Until then the binary states its shape and exits with EX_USAGE,
// so a premature CI wiring fails loudly instead of green-without-running.
Console.Error.WriteLine("meridian: the collection runner has not shipped yet.");
Console.Error.WriteLine("planned usage: meridian run <collection> --env <name> [--report junit:<file>] [--bail]");
return 64;
