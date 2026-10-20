// Minimal CLI entrypoint. The complete interactive C# implementation is in
// app_console.cs, mirroring the app_console.c module in the original repository.
internal static class Program
{
    private static int Main(string[] args) => AppConsole.Run(args);
}
