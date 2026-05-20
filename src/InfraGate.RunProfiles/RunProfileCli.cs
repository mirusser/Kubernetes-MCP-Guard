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
            string profileName;
            string outputPath;
            bool force;
            IReadOnlyList<(string Path, string Value)> setOverrides;
            RunProfile profile;
            try
            {
                profileName = GetRequiredProfileName(args);
                outputPath = GetRequiredOption(args, RunProfileConventions.Options.Output);
                force = HasFlag(args, RunProfileConventions.Options.Force);
                setOverrides = GetSetOverrides(args);
                profile = document.FindProfileWithDefaults(profileName, document.Defaults);
                profile = ApplySetOverrides(profile, setOverrides);
            }
            catch (InvalidOperationException ex)
            {
                await error.WriteLineAsync(ex.Message).ConfigureAwait(false);
                return 1;
            }

            if (File.Exists(outputPath) && !force)
            {
                string? firstLine = await ReadFirstLineAsync(outputPath, cancellationToken).ConfigureAwait(false);
                bool isGenerated = firstLine?.StartsWith(RunProfileConventions.GeneratedFile.HeaderLinePrefix, StringComparison.Ordinal) == true;
                bool isCorrectProfile = firstLine?.EndsWith($"{RunProfileConventions.GeneratedFile.ProfileMarker}{profileName}", StringComparison.Ordinal) == true;

                if (!isGenerated || !isCorrectProfile)
                {
                    await error.WriteLineAsync(
                        $"Will not overwrite '{outputPath}': not generated for profile '{profileName}'. Use --force to overwrite.").ConfigureAwait(false);
                    return 1;
                }
            }

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

    private static bool HasFlag(IReadOnlyList<string> args, string flag)
    {
        foreach (string arg in args)
        {
            if (string.Equals(arg, flag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<string?> ReadFirstLineAsync(string path, CancellationToken cancellationToken)
    {
        using var streamReader = new StreamReader(path);
        return await streamReader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
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

    private static IReadOnlyList<(string Path, string Value)> GetSetOverrides(IReadOnlyList<string> args)
    {
        var overrides = new List<(string, string)>();
        for (int i = 1; i < args.Count; i++)
        {
            if (!string.Equals(args[i], RunProfileConventions.Options.Set, StringComparison.Ordinal))
            {
                continue;
            }

            if (i + 1 >= args.Count)
            {
                throw new InvalidOperationException($"{RunProfileConventions.Options.Set} requires a value.");
            }

            string assignment = args[++i];
            int eq = assignment.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                throw new InvalidOperationException(
                    $"{RunProfileConventions.Options.Set} value must be in path=value format: {assignment}");
            }

            overrides.Add((assignment[..eq], assignment[(eq + 1)..]));
        }

        return overrides;
    }

    private static RunProfile ApplySetOverrides(
        RunProfile profile,
        IReadOnlyList<(string Path, string Value)> overrides)
    {
        foreach ((string path, string value) in overrides)
        {
            profile = ApplySetOverride(profile, path, value);
        }

        return profile;
    }

    private static RunProfile ApplySetOverride(RunProfile profile, string path, string value)
    {
        int dot = path.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
        {
            throw new InvalidOperationException($"Unknown --set path: {path}");
        }

        string section = path[..dot];
        string field = path[(dot + 1)..];

        return section switch
        {
            RunProfileConventions.YamlKeys.DownstreamAuth => profile with
            {
                DownstreamAuth = ApplyDownstreamAuthOverride(
                    profile.DownstreamAuth ?? new DownstreamAuthProfile(null, null, null, null, null, null, null, null),
                    field, value, path)
            },
            RunProfileConventions.YamlKeys.Gateway => profile with
            {
                Gateway = ApplyGatewayOverride(
                    profile.Gateway ?? new GatewayProfile(null, null, null), field, value, path)
            },
            RunProfileConventions.YamlKeys.IdentityProvider => profile with
            {
                IdentityProvider = ApplyIdentityProviderOverride(
                    profile.IdentityProvider ?? new IdentityProviderProfile(null, null, null, null, null, null), field, value, path)
            },
            RunProfileConventions.YamlKeys.ApprovalAuthority => profile with
            {
                ApprovalAuthority = ApplyApprovalAuthorityOverride(
                    profile.ApprovalAuthority ?? new ApprovalAuthorityProfile(null, null, null, null, null), field, value, path)
            },
            RunProfileConventions.YamlKeys.GenericApprovalCore => profile with
            {
                GenericApprovalCore = ApplyGenericApprovalCoreOverride(
                    profile.GenericApprovalCore ?? new GenericApprovalCoreProfile(string.Empty), field, value, path)
            },
            RunProfileConventions.YamlKeys.Host => profile with
            {
                Host = ApplyHostOverride(
                    profile.Host ?? new HostProfile(null, null, null, null, null, null, null), field, value, path)
            },
            _ => throw new InvalidOperationException($"Unknown --set path: {path}")
        };
    }

    private static DownstreamAuthProfile ApplyDownstreamAuthOverride(
        DownstreamAuthProfile profile, string field, string value, string path) =>
        field switch
        {
            RunProfileConventions.YamlKeys.Required => profile with { Required = value },
            RunProfileConventions.YamlKeys.Authority => profile with { Authority = value },
            RunProfileConventions.YamlKeys.MetadataAddress => profile with { MetadataAddress = value },
            RunProfileConventions.YamlKeys.RequireHttpsMetadata => profile with { RequireHttpsMetadata = value },
            RunProfileConventions.YamlKeys.Audience => profile with { Audience = value },
            RunProfileConventions.YamlKeys.Scope => profile with { Scope = value },
            RunProfileConventions.YamlKeys.GatewayClientId => profile with { GatewayClientId = value },
            RunProfileConventions.YamlKeys.GatewayClientSecret => profile with { GatewayClientSecret = value },
            _ => throw new InvalidOperationException($"Unknown --set path: {path}")
        };

    private static GatewayProfile ApplyGatewayOverride(
        GatewayProfile profile, string field, string value, string path) =>
        field switch
        {
            RunProfileConventions.YamlKeys.AspnetcoreUrls => profile with { AspnetcoreUrls = value },
            RunProfileConventions.YamlKeys.DownstreamAssembly => profile with { DownstreamAssembly = value },
            RunProfileConventions.YamlKeys.GuardAuditRoot => profile with { GuardAuditRoot = value },
            _ => throw new InvalidOperationException($"Unknown --set path: {path}")
        };

    private static IdentityProviderProfile ApplyIdentityProviderOverride(
        IdentityProviderProfile profile, string field, string value, string path) =>
        field switch
        {
            RunProfileConventions.YamlKeys.RealmImport => profile with { RealmImport = value },
            RunProfileConventions.YamlKeys.Authority => profile with { Authority = value },
            RunProfileConventions.YamlKeys.MetadataAddress => profile with { MetadataAddress = value },
            RunProfileConventions.YamlKeys.Resource => profile with { Resource = value },
            RunProfileConventions.YamlKeys.Scope => profile with { Scope = value },
            RunProfileConventions.YamlKeys.RequireHttpsMetadata => profile with { RequireHttpsMetadata = value },
            _ => throw new InvalidOperationException($"Unknown --set path: {path}")
        };

    private static ApprovalAuthorityProfile ApplyApprovalAuthorityOverride(
        ApprovalAuthorityProfile profile, string field, string value, string path) =>
        field switch
        {
            RunProfileConventions.YamlKeys.BaseUrl => profile with { BaseUrl = value },
            RunProfileConventions.YamlKeys.OauthClientId => profile with { OauthClientId = value },
            RunProfileConventions.YamlKeys.OauthCallbackPath => profile with { OauthCallbackPath = value },
            RunProfileConventions.YamlKeys.OauthAuthorizationEndpoint => profile with { OauthAuthorizationEndpoint = value },
            RunProfileConventions.YamlKeys.OauthTokenEndpoint => profile with { OauthTokenEndpoint = value },
            _ => throw new InvalidOperationException($"Unknown --set path: {path}")
        };

    private static GenericApprovalCoreProfile ApplyGenericApprovalCoreOverride(
        GenericApprovalCoreProfile profile, string field, string value, string path) =>
        field switch
        {
            RunProfileConventions.YamlKeys.ApprovalRoot => profile with { ApprovalRoot = value },
            _ => throw new InvalidOperationException($"Unknown --set path: {path}")
        };

    private static HostProfile ApplyHostOverride(
        HostProfile profile, string field, string value, string path) =>
        field switch
        {
            RunProfileConventions.YamlKeys.BindAddress => profile with { BindAddress = value },
            RunProfileConventions.YamlKeys.BindPort => profile with { BindPort = value },
            RunProfileConventions.YamlKeys.GatewayImage => profile with { GatewayImage = value },
            RunProfileConventions.YamlKeys.KubeconfigHostPath => profile with { KubeconfigHostPath = value },
            RunProfileConventions.YamlKeys.ApprovalHostPath => profile with { ApprovalHostPath = value },
            RunProfileConventions.YamlKeys.GuardAuditHostPath => profile with { GuardAuditHostPath = value },
            RunProfileConventions.YamlKeys.DataProtectionHostPath => profile with { DataProtectionHostPath = value },
            _ => throw new InvalidOperationException($"Unknown --set path: {path}")
        };
}
