using Spectre.Console.Cli;

namespace AssemblySizeAnalyzer;

internal static class Program
{
    private static int Main(string[] args)
    {
        var app = new CommandApp<AnalyzeAssemblySizeCommand>();
        return app.Run(args);
    }
}
