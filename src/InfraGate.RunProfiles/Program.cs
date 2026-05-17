namespace InfraGate.RunProfiles;

internal static class Program
{
    public static Task<int> Main(string[] args) =>
        RunProfileCli.ExecuteAsync(args, Console.Out, Console.Error, CancellationToken.None);
}
