namespace InfraGate.RunProfiles;

internal static class RunProfileCli
{
    public static async Task<int> ExecuteAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Count == 0)
        {
            await error.WriteLineAsync("Command is required.").ConfigureAwait(false);
            return 1;
        }

        string command = args[0];
        if (!IsKnownCommand(command))
        {
            await error.WriteLineAsync($"Unknown command: {command}").ConfigureAwait(false);
            return 1;
        }

        string configPath;
        RunProfileDocument document;
        try
        {
            configPath = GetConfigPath(args);
            document = await RunProfileDocumentReader.ReadAsync(configPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            await error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }

        if (string.Equals(command, RunProfileConventions.Commands.Validate, StringComparison.Ordinal))
        {
            await output.WriteLineAsync("Run profile configuration is valid.").ConfigureAwait(false);
            return 0;
        }

        if (string.Equals(command, RunProfileConventions.Commands.Generate, StringComparison.Ordinal))
        {
            string profileName = GetRequiredProfileName(args);
            string outputPath = GetRequiredOption(args, RunProfileConventions.Options.Output);
            RunProfile profile = document.FindProfile(profileName);
            string envText = EnvFileRenderer.Render(Path.GetFileName(configPath), profile);
            string? outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            await File.WriteAllTextAsync(outputPath, envText, cancellationToken).ConfigureAwait(false);
            await output.WriteLineAsync($"Generated {outputPath}").ConfigureAwait(false);
            return 0;
        }

        foreach (RunProfile profile in document.Profiles)
        {
            await output.WriteLineAsync($"{profile.Name}\t{profile.Kind}").ConfigureAwait(false);
        }

        return 0;
    }

    private static bool IsKnownCommand(string command) =>
        string.Equals(command, RunProfileConventions.Commands.Generate, StringComparison.Ordinal) ||
        string.Equals(command, RunProfileConventions.Commands.List, StringComparison.Ordinal) ||
        string.Equals(command, RunProfileConventions.Commands.Validate, StringComparison.Ordinal);

    private static string GetRequiredProfileName(IReadOnlyList<string> args)
    {
        if (args.Count < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Profile name is required.");
        }

        return args[1];
    }

    private static string GetConfigPath(IReadOnlyList<string> args)
    {
        string? configuredPath = GetOption(args, RunProfileConventions.Options.Config);
        return configuredPath ?? ResolveDefaultConfigPath();
    }

    private static string GetRequiredOption(IReadOnlyList<string> args, string option)
    {
        return GetOption(args, option) ??
            throw new InvalidOperationException($"{option} requires a value.");
    }

    private static string? GetOption(IReadOnlyList<string> args, string option)
    {
        for (int i = 1; i < args.Count; i++)
        {
            if (string.Equals(args[i], option, StringComparison.Ordinal))
            {
                if (i + 1 >= args.Count)
                {
                    throw new InvalidOperationException($"{option} requires a value.");
                }

                return args[i + 1];
            }
        }

        return null;
    }

    private static string ResolveDefaultConfigPath()
    {
        string currentDirectory = Directory.GetCurrentDirectory();
        DirectoryInfo? directory = new(currentDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, RunProfileConventions.DefaultConfigPath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return RunProfileConventions.DefaultConfigPath;
    }
}
