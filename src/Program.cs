using Spectre.Console.Cli;

namespace AssemblySizeAnalyzer;

internal static class Program
{
    private static int Main(string[] args)
    {
        var app = new CommandApp<AnalyzeCommand>();
        return app.Run(args);
    }
}
