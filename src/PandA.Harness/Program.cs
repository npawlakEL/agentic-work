using PandA.Sim;

// Bare-bones interactive PandA message simulator. On startup it seeds a line + advised cartons, lists the
// carton blind labels, and lets you view a carton's data or run it (281 induct → print → 286 verify).
var host = new SimHost();

Console.WriteLine("PandA message sim — bare-bones slice (281 induct → print → 286 verify)");
Console.WriteLine("Commands: [l]ist  [v]iew <n>  [r]un <n>  [s]tatus  [p]rinter <id> up|down  [z]one up|down  [q]uit");
Console.WriteLine();
PrintList(host);

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine();
    if (input is null)
    {
        break; // EOF (piped/non-interactive)
    }

    var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0)
    {
        continue;
    }

    var cmd = parts[0].ToLowerInvariant();
    if (cmd is "q" or "quit" or "exit")
    {
        break;
    }

    if (cmd is "l" or "list")
    {
        PrintList(host);
        continue;
    }

    if (cmd is "v" or "view" or "r" or "run")
    {
        if (!TryResolve(host, parts, out var blind))
        {
            Console.WriteLine("  usage: " + cmd + " <n>   (n = carton number from the list)");
            continue;
        }

        if (cmd is "v" or "view")
        {
            Console.WriteLine(host.Describe(blind));
        }
        else
        {
            foreach (var line in await host.RunAsync(blind))
            {
                Console.WriteLine(line);
            }
        }

        Console.WriteLine();
        continue;
    }

    if (cmd is "s" or "status")
    {
        Console.WriteLine(host.PrinterStatusReport());
        Console.WriteLine();
        continue;
    }

    if (cmd is "p" or "printer")
    {
        if (parts.Length < 3 || parts[2].ToLowerInvariant() is not ("up" or "down"))
        {
            Console.WriteLine("  usage: p <printerId> up|down");
            continue;
        }

        foreach (var line in host.SetPrinterStatus(parts[1], online: parts[2].ToLowerInvariant() == "up"))
        {
            Console.WriteLine(line);
        }

        Console.WriteLine();
        continue;
    }

    if (cmd is "z" or "zone")
    {
        if (parts.Length < 2 || parts[1].ToLowerInvariant() is not ("up" or "down"))
        {
            Console.WriteLine("  usage: z up|down");
            continue;
        }

        foreach (var line in host.SetZoneStatus(online: parts[1].ToLowerInvariant() == "up"))
        {
            Console.WriteLine(line);
        }

        Console.WriteLine();
        continue;
    }

    Console.WriteLine("  unknown command. try: l, v <n>, r <n>, s, p <id> up|down, z up|down, q");
}

static void PrintList(SimHost host)
{
    Console.WriteLine("Seeded cartons:");
    for (var i = 0; i < host.BlindLabels.Count; i++)
    {
        Console.WriteLine($"  [{i + 1}] {host.BlindLabels[i]}");
    }

    Console.WriteLine();
}

static bool TryResolve(SimHost host, string[] parts, out string blindLabel)
{
    blindLabel = string.Empty;
    if (parts.Length < 2 || !int.TryParse(parts[1], out var n) || n < 1 || n > host.BlindLabels.Count)
    {
        return false;
    }

    blindLabel = host.BlindLabels[n - 1];
    return true;
}
