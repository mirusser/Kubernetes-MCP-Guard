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

        if (!TryLoadConfig(args, error, out string configPath, out RunProfileDocument document))
        {
            return 1;
        }

        return await DispatchCommandAsync(command, args, document, configPath, output, error, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool TryLoadConfig(
        IReadOnlyList<string> args,
        TextWriter error,
        out string configPath,
        out RunProfileDocument document)
    {
        try
        {
            configPath = GetConfigPath(args);
            document = RunProfileDocumentReader.ReadAsync(configPath, CancellationToken.None).GetAwaiter().GetResult();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            error.WriteLineAsync(ex.Message).GetAwaiter().GetResult();
            configPath = string.Empty;
            document = null!;
            return false;
        }
    }

    private static async Task<int> DispatchCommandAsync(
        string command,
        IReadOnlyList<string> args,
        RunProfileDocument document,
        string configPath,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (string.Equals(command, RunProfileConventions.Commands.Validate, StringComparison.Ordinal))
        {
            await output.WriteLineAsync("Run profile configuration is valid.").ConfigureAwait(false);
            return 0;
        }

        if (string.Equals(command, RunProfileConventions.Commands.Generate, StringComparison.Ordinal))
        {
            return await HandleGenerateCommandAsync(args, document, configPath, output, error, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (RunProfile profile in document.Profiles)
        {
            await output.WriteLineAsync($"{profile.Name}\t{profile.Kind}").ConfigureAwait(false);
        }

        return 0;
    }

    private static async Task<int> HandleGenerateCommandAsync(
        IReadOnlyList<string> args,
        RunProfileDocument document,
        string configPath,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (!TryParseGenerateArgs(args, document, error, out var parsed))
        {
            return 1;
        }

        if (!parsed.Force && !await CheckFileOverwriteAllowedAsync(parsed.OutputPath, parsed.ProfileName, cancellationToken)
            .ConfigureAwait(false))
        {
            await error.WriteLineAsync(
                $"Will not overwrite '{parsed.OutputPath}': not generated for profile '{parsed.ProfileName}'. Use --force to overwrite.")
                .ConfigureAwait(false);
            return 1;
        }

        string generatedText;
        try
        {
            generatedText = EnvFileRenderer.Render(Path.GetFileName(configPath), parsed.Profile);
        }
        catch (InvalidOperationException ex)
        {
            await error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }

        string? outputDirectory = Path.GetDirectoryName(parsed.OutputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await File.WriteAllTextAsync(parsed.OutputPath, generatedText, cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync($"Generated {parsed.OutputPath}").ConfigureAwait(false);
        return 0;
    }

    private sealed record class GenerateArgs(
        string ProfileName,
        string OutputPath,
        bool Force,
        RunProfile Profile);

    private static bool TryParseGenerateArgs(
        IReadOnlyList<string> args,
        RunProfileDocument document,
        TextWriter error,
        out GenerateArgs parsed)
    {
        try
        {
            var profileName = GetRequiredProfileName(args);
            var outputPath = GetRequiredOption(args, RunProfileConventions.Options.Output);
            RejectRemovedFormatOption(args);
            var force = HasFlag(args, RunProfileConventions.Options.Force);
            var setOverrides = GetSetOverrides(args);
            var profile = document.FindProfileWithDefaults(profileName, document.Defaults);
            profile = ApplySetOverrides(profile, setOverrides);
            parsed = new GenerateArgs(profileName, outputPath, force, profile);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            error.WriteLineAsync(ex.Message).GetAwaiter().GetResult();
            parsed = null!;
            return false;
        }
    }

    private static async Task<bool> CheckFileOverwriteAllowedAsync(
        string outputPath, string profileName, CancellationToken cancellationToken)
    {
        if (!File.Exists(outputPath))
        {
            return true;
        }

        string existingContent = await File.ReadAllTextAsync(outputPath, cancellationToken).ConfigureAwait(false);
        return IsGeneratedForProfile(existingContent, profileName);
    }

    private static bool HasFlag(IReadOnlyList<string> args, string flag) =>
        args.Any(arg => string.Equals(arg, flag, StringComparison.Ordinal));

    private static void RejectRemovedFormatOption(IReadOnlyList<string> args)
    {
        if (GetOption(args, RunProfileConventions.Options.Format) is null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{RunProfileConventions.Options.Format} is no longer supported; generate writes one env file.");
    }

    private static bool IsGeneratedForProfile(string content, string profileName) =>
        IsGeneratedEnvForProfile(content, profileName);

    private static bool IsGeneratedEnvForProfile(string content, string profileName)
    {
        using var reader = new StringReader(content);
        string? firstLine = reader.ReadLine();
        return firstLine?.StartsWith(RunProfileConventions.GeneratedFile.HeaderLinePrefix, StringComparison.Ordinal) == true &&
            firstLine.EndsWith($"{RunProfileConventions.GeneratedFile.ProfileMarker}{profileName}", StringComparison.Ordinal);
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

    // NOSONAR:S3267 — The while-loop here advances by 2 for --set flag+value pairs, throws on
    // invalid state, and parses path=value format. LINQ would harm readability.
    private static IReadOnlyList<(string Path, string Value)> GetSetOverrides(IReadOnlyList<string> args)
    {
        var overrides = new List<(string, string)>();
        int i = 1;
        while (i < args.Count)
        {
            if (!string.Equals(args[i], RunProfileConventions.Options.Set, StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            if (i + 1 >= args.Count)
            {
                throw new InvalidOperationException($"{RunProfileConventions.Options.Set} requires a value.");
            }

            i++;
            string assignment = args[i];
            int eq = assignment.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                throw new InvalidOperationException(
                    $"{RunProfileConventions.Options.Set} value must be in path=value format: {assignment}");
            }

            overrides.Add((assignment[..eq], assignment[(eq + 1)..]));
            i++;
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
            RunProfileConventions.YamlKeys.OpenRouter => profile with
            {
                OpenRouter = ApplyOpenRouterOverride(
                    profile.OpenRouter ?? new OpenRouterProfile(null), field, value, path)
            },
            RunProfileConventions.YamlKeys.Observer => profile with
            {
                Observer = ApplyObserverOverride(
                    profile.Observer ?? new ObserverProfile(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null), field, value, path)
            },
            RunProfileConventions.YamlKeys.Planner => profile with
            {
                Planner = ApplyPlannerOverride(
                    profile.Planner ?? new PlannerProfile(null, null, null, null, null, null, null, null, null, null, null, null, null, null), field, value, path)
            },
            RunProfileConventions.YamlKeys.Executor => profile with
            {
                Executor = ApplyExecutorOverride(
                    profile.Executor ?? new ExecutorProfile(null, null, null, null, null, null, null, null, null), field, value, path)
            },
            RunProfileConventions.YamlKeys.AgentGuardrails => profile with
            {
                AgentGuardrails = ApplyAgentGuardrailsOverride(
                    profile.AgentGuardrails ?? new AgentGuardrailsProfile(null), field, value, path)
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
            RunProfileConventions.YamlKeys.PostgresConnectionString => profile with { PostgresConnectionString = value },
            RunProfileConventions.YamlKeys.RunMigrationsOnStartup => profile with { RunMigrationsOnStartup = bool.TryParse(value, out bool parsed) ? parsed : null },
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

    private static OpenRouterProfile ApplyOpenRouterOverride(
        OpenRouterProfile profile, string field, string value, string path) =>
        field switch
        {
            RunProfileConventions.YamlKeys.ApiKey => profile with { ApiKey = value },
            _ => throw new InvalidOperationException($"Unknown --set path: {path}")
        };

    private static ObserverProfile ApplyObserverOverride(
        ObserverProfile profile, string field, string value, string path) =>
        field switch
        {
            RunProfileConventions.YamlKeys.AspnetcoreUrls => profile with { AspnetcoreUrls = value },
            RunProfileConventions.YamlKeys.GatewayBaseUrl => profile with { GatewayBaseUrl = value },
            RunProfileConventions.YamlKeys.OAuthAuthority => profile with { OAuthAuthority = value },
            RunProfileConventions.YamlKeys.ClientId => profile with { ClientId = value },
            RunProfileConventions.YamlKeys.ClientSecret => profile with { ClientSecret = value },
            RunProfileConventions.YamlKeys.Scope => profile with { Scope = value },
            RunProfileConventions.YamlKeys.LlmProvider => profile with { LlmProvider = value },
            RunProfileConventions.YamlKeys.LlmModel => profile with { LlmModel = value },
            RunProfileConventions.YamlKeys.CycleCadenceSeconds => profile with { CycleCadenceSeconds = value },
            RunProfileConventions.YamlKeys.CycleWallClockCapSeconds => profile with { CycleWallClockCapSeconds = value },
            RunProfileConventions.YamlKeys.MaxToolIterations => profile with { MaxToolIterations = value },
            RunProfileConventions.YamlKeys.FileSinkRoot => profile with { FileSinkRoot = value },
            RunProfileConventions.YamlKeys.PlannerHandoffUrl => profile with { PlannerHandoffUrl = value },
            RunProfileConventions.YamlKeys.ObserverHostPath => profile with { ObserverHostPath = value },
            RunProfileConventions.YamlKeys.ObserverAuditConnectionString => profile with { AuditConnectionString = value },
            _ => throw new InvalidOperationException($"Unknown --set path: {path}")
        };

    private static PlannerProfile ApplyPlannerOverride(
        PlannerProfile profile, string field, string value, string path) =>
        field switch
        {
            RunProfileConventions.YamlKeys.AspnetcoreUrls => profile with { AspnetcoreUrls = value },
            RunProfileConventions.YamlKeys.GatewayBaseUrl => profile with { GatewayBaseUrl = value },
            RunProfileConventions.YamlKeys.ExecutorHandoffUrl => profile with { ExecutorHandoffUrl = value },
            RunProfileConventions.YamlKeys.ClientId => profile with { ClientId = value },
            RunProfileConventions.YamlKeys.ClientSecret => profile with { ClientSecret = value },
            RunProfileConventions.YamlKeys.OAuthAuthority => profile with { OAuthAuthority = value },
            RunProfileConventions.YamlKeys.Scope => profile with { OAuthScope = value },
            RunProfileConventions.YamlKeys.LlmProvider => profile with { LlmProvider = value },
            RunProfileConventions.YamlKeys.LlmModel => profile with { LlmModel = value },
            RunProfileConventions.YamlKeys.AnomalyWallClockCapSeconds => profile with { AnomalyWallClockCapSeconds = value },
            RunProfileConventions.YamlKeys.BatchWallClockCapSeconds => profile with { BatchWallClockCapSeconds = value },
            RunProfileConventions.YamlKeys.MaxToolIterations => profile with { MaxToolIterations = value },
            RunProfileConventions.YamlKeys.FileSinkRoot => profile with { FileSinkRoot = value },
            RunProfileConventions.YamlKeys.PlannerHostPath => profile with { PlannerHostPath = value },
            _ => throw new InvalidOperationException($"Unknown --set path: {path}")
        };

    private static ExecutorProfile ApplyExecutorOverride(
        ExecutorProfile profile, string field, string value, string path) =>
        field switch
        {
            RunProfileConventions.YamlKeys.AspnetcoreUrls => profile with { AspnetcoreUrls = value },
            RunProfileConventions.YamlKeys.GatewayBaseUrl => profile with { GatewayBaseUrl = value },
            RunProfileConventions.YamlKeys.ClientId => profile with { ClientId = value },
            RunProfileConventions.YamlKeys.ClientSecret => profile with { ClientSecret = value },
            RunProfileConventions.YamlKeys.OAuthAuthority => profile with { OAuthAuthority = value },
            RunProfileConventions.YamlKeys.Scope => profile with { OAuthScope = value },
            RunProfileConventions.YamlKeys.ConcurrencyCap => profile with { ConcurrencyCap = value },
            RunProfileConventions.YamlKeys.WatchTimeoutSeconds => profile with { WatchTimeoutSeconds = value },
            RunProfileConventions.YamlKeys.ExecutorHostPath => profile with { ExecutorHostPath = value },
            _ => throw new InvalidOperationException($"Unknown --set path: {path}")
        };

    private static AgentGuardrailsProfile ApplyAgentGuardrailsOverride(
        AgentGuardrailsProfile profile, string field, string value, string path)
    {
        int dot = field.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
        {
            throw new InvalidOperationException($"Unknown --set path: {path}");
        }

        string subSection = field[..dot];
        string subField = field[(dot + 1)..];

        if (subSection.Equals(RunProfileConventions.YamlKeys.ModelVisibleContent, StringComparison.Ordinal))
        {
            return profile with
            {
                ModelVisibleContent = ApplyModelVisibleContentOverride(
                    profile.ModelVisibleContent ?? new ModelVisibleContentProfile(null, null, null, null, null),
                    subField, value, path)
            };
        }

        throw new InvalidOperationException($"Unknown --set path: {path}");
    }

    private static ModelVisibleContentProfile ApplyModelVisibleContentOverride(
        ModelVisibleContentProfile profile, string field, string value, string path) =>
        field switch
        {
            var f when f.Equals(RunProfileConventions.YamlKeys.Enabled, StringComparison.Ordinal) =>
                profile with { Enabled = value },
            var f when f.Equals(RunProfileConventions.YamlKeys.SemanticClassifierEnabled, StringComparison.Ordinal) =>
                profile with { SemanticClassifierEnabled = value },
            var f when f.Equals(RunProfileConventions.YamlKeys.RequestTimeoutMilliseconds, StringComparison.Ordinal) =>
                profile with { RequestTimeoutMilliseconds = value },
            var f when f.Equals(RunProfileConventions.YamlKeys.MaximumInputCharacters, StringComparison.Ordinal) =>
                profile with { MaximumInputCharacters = value },
            var f when f.Equals(RunProfileConventions.YamlKeys.UnavailableBehavior, StringComparison.Ordinal) =>
                profile with { UnavailableBehavior = value },
            _ => throw new InvalidOperationException($"Unknown --set path: {path}")
        };
}
