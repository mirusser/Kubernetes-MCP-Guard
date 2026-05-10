namespace InfraGate.RuntimeSafety;

public static class ProductionSafetyValidator
{
    private static readonly UnixFileMode GroupOrOtherWrite =
        UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;

    public static void RequireHttpsNonLoopbackUri(string? value, string settingName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{settingName} is required in Production mode.");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            throw new InvalidOperationException($"{settingName} must be an absolute URI in Production mode.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{settingName} must use https in Production mode.");
        }

        if (uri.IsLoopback)
        {
            throw new InvalidOperationException($"{settingName} must not point at a loopback host in Production mode.");
        }
    }

    public static void RequirePersistentDirectory(
        string path,
        string settingName,
        bool isExplicit,
        IReadOnlySet<string> deniedDirectoryNames)
    {
        RequireExplicitPersistentPath(path, settingName, isExplicit);
        string fullPath = GetValidatedFullPath(path, settingName);

        RequireNonTempDirectory(fullPath, settingName);
        RequireNonDefaultDirectoryName(fullPath, settingName, deniedDirectoryNames);
        RequireSecureExistingDirectory(fullPath, settingName);
    }

    private static void RequireExplicitPersistentPath(string path, string settingName, bool isExplicit)
    {
        if (!isExplicit)
        {
            throw new InvalidOperationException($"{settingName} must be explicitly configured in Production mode.");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"{settingName} must not be empty in Production mode.");
        }

        if (!Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException($"{settingName} must be an absolute path in Production mode.");
        }
    }

    private static string GetValidatedFullPath(string path, string settingName)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"{settingName} must be a valid path in Production mode.", ex);
        }
        catch (NotSupportedException ex)
        {
            throw new InvalidOperationException($"{settingName} must be a valid path in Production mode.", ex);
        }
    }

    private static void RequireNonTempDirectory(string fullPath, string settingName)
    {
        if (IsUnderPath(fullPath, Path.GetTempPath()))
        {
            throw new InvalidOperationException($"{settingName} must not be under the temp directory in Production mode.");
        }
    }

    private static void RequireNonDefaultDirectoryName(
        string fullPath,
        string settingName,
        IReadOnlySet<string> deniedDirectoryNames)
    {
        string leafDirectory = new DirectoryInfo(fullPath).Name;
        if (deniedDirectoryNames.Contains(leafDirectory))
        {
            throw new InvalidOperationException($"{settingName} must not use the default development directory in Production mode.");
        }
    }

    private static void RequireSecureExistingDirectory(string fullPath, string settingName)
    {
        if (OperatingSystem.IsWindows() || !Directory.Exists(fullPath))
        {
            return;
        }

        UnixFileMode mode = File.GetUnixFileMode(fullPath);
        if ((mode & GroupOrOtherWrite) != 0)
        {
            throw new InvalidOperationException(
                $"{settingName} must not be group- or other-writable in Production mode.");
        }
    }

    private static bool IsUnderPath(string path, string parentPath)
    {
        string normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalizedParent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        return normalizedPath.StartsWith(normalizedParent, StringComparison.Ordinal);
    }
}
