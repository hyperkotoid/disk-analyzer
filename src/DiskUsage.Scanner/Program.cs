using DiskUsage.Scanner;

var socketPath = Argument(args, "--socket");
var root = Argument(args, "--root") ?? "/";
if (string.IsNullOrWhiteSpace(socketPath))
{
    Console.Error.WriteLine("Нужен параметр --socket.");
    return 1;
}

try
{
    return await ScannerSession.RunAsync(socketPath, root);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static string? Argument(string[] args, string name)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (args[index] == name)
            return args[index + 1];
    }

    return null;
}
