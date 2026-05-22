using System.Text.Json;

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
        string profileName;
        string outputPath;
        string format;
        bool force;
        IReadOnlyList<(string Path, string Value)> setOverrides;
        RunProfile profile;
        try
        {
            profileName = GetRequiredProfileName(args);
            outputPath = GetRequiredOption(args, RunProfileConventions.Options.Output);
            format = GetGenerateFormat(args);
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
            string existingContent = await File.ReadAllTextAsync(outputPath, cancellationToken).ConfigureAwait(false);
            if (!IsGeneratedForProfile(existingContent, profileName))
            {
                await error.WriteLineAsync(
                    $"Will not overwrite '{outputPath}': not generated for profile '{profileName}'. Use --force to overwrite.").ConfigureAwait(false);
                return 1;
            }
        }

        string generatedText;
        try
        {
            generatedText = format switch
            {
                RunProfileConventions.Formats.AppSettingJson => AppSettingsRenderer.Render(Path.GetFileName(configPath), profile),
                RunProfileConventions.Formats.DotEnv => EnvFileRenderer.Render(Path.GetFileName(configPath), profile),
                _ => throw new InvalidOperationException($"Unsupported format: {format}")
            };
        }
        catch (InvalidOperationException ex)
        {
            await error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }

        string? outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await File.WriteAllTextAsync(outputPath, generatedText, cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync($"Generated {outputPath}").ConfigureAwait(false);
        return 0;
    }

    private static bool HasFlag(IReadOnlyList<string> args, string flag) =>
        args.Any(arg => string.Equals(arg, flag, StringComparison.Ordinal));

    private static string GetGenerateFormat(IReadOnlyList<string> args)
    {
        string? format = GetOption(args, RunProfileConventions.Options.Format);
        if (format is null)
        {
            return RunProfileConventions.Formats.DotEnv;
        }

        if (string.Equals(format, RunProfileConventions.Formats.DotEnv, StringComparison.Ordinal) ||
            string.Equals(format, RunProfileConventions.Formats.AppSettingJson, StringComparison.Ordinal))
        {
            return format;
        }

        throw new InvalidOperationException(
            $"{RunProfileConventions.Options.Format} must be '{RunProfileConventions.Formats.DotEnv}' or '{RunProfileConventions.Formats.AppSettingJson}'.");
    }

    private static bool IsGeneratedForProfile(string content, string profileName) =>
        IsGeneratedEnvForProfile(content, profileName) ||
        IsGeneratedAppSettingsForProfile(content, profileName);

    private static bool IsGeneratedEnvForProfile(string content, string profileName)
    {
        using var reader = new StringReader(content);
        string? firstLine = reader.ReadLine();
        return firstLine?.StartsWith(RunProfileConventions.GeneratedFile.HeaderLinePrefix, StringComparison.Ordinal) == true &&
            firstLine.EndsWith($"{RunProfileConventions.GeneratedFile.ProfileMarker}{profileName}", StringComparison.Ordinal);
    }

    private static bool IsGeneratedAppSettingsForProfile(string content, string profileName)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            return document.RootElement.TryGetProperty(
                    RunProfileConventions.GeneratedFile.MetadataSection,
                    out JsonElement metadata) &&
                metadata.TryGetProperty(RunProfileConventions.GeneratedFile.MetadataProfile, out JsonElement profile) &&
                string.Equals(profile.GetString(), profileName, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
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
                    profile.Host ?? new HostProfile(null, null, null, null, null, null, null, null), field, value, path)
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
            RunProfileConventions.YamlKeys.ConfigHostPath => profile with { ConfigHostPath = value },
            RunProfileConventions.YamlKeys.KubeconfigHostPath => profile with { KubeconfigHostPath = value },
            RunProfileConventions.YamlKeys.ApprovalHostPath => profile with { ApprovalHostPath = value },
            RunProfileConventions.YamlKeys.GuardAuditHostPath => profile with { GuardAuditHostPath = value },
            RunProfileConventions.YamlKeys.DataProtectionHostPath => profile with { DataProtectionHostPath = value },
            _ => throw new InvalidOperationException($"Unknown --set path: {path}")
        };
}
