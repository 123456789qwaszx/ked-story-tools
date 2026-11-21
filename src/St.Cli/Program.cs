namespace St.Cli;

internal static class Program
{
    private static int Main(string[] args) => StCli.Run(args, Console.Out, Console.Error);
}
