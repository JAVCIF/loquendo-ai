using LoquendoAI.Infrastructure.Projects;

if (args.Length == 0)
{
    PrintHelp();
    return;
}

var projects = new ProjectService();

switch (args[0].ToLowerInvariant())
{
    case "init" when args.Length >= 3:
    {
        await using var repo = await projects.CreateAsync(args[1], args[2]);
        Console.WriteLine($"Proyecto creado: {repo.ProjectRoot}");
        Console.WriteLine($"ID: {repo.Manifest.Id}");
        break;
    }

    case "inspect" when args.Length >= 2:
    {
        await using var repo = await projects.OpenAsync(args[1]);
        var assets = await repo.GetAssetsAsync();
        var sources = await repo.GetAssetSourcesAsync();
        Console.WriteLine($"{repo.Manifest.Name} | schema {repo.Manifest.SchemaVersion}");
        Console.WriteLine($"Root: {repo.ProjectRoot}");
        Console.WriteLine($"Fuentes: {sources.Count}");
        Console.WriteLine($"Assets: {assets.Count}");
        Console.WriteLine($"Faltantes: {assets.Count(a => a.IsMissing)}");
        break;
    }

    default:
        PrintHelp();
        break;
}

static void PrintHelp()
{
    Console.WriteLine("Loquendo AI CLI v0.0.2");
    Console.WriteLine("  init <carpeta-padre> <nombre-proyecto>");
    Console.WriteLine("  inspect <carpeta-proyecto>");
}
