using System.Reflection;
using System.Text;
using InfraGate.Remediation.Contracts;

namespace InfraGate.Remediation.Contracts.Tests.UnitTests;

public sealed class PublicApiSnapshotTests
{
    [Fact]
    public void PublicApiMatchesCommittedBaseline()
    {
        var actual = GeneratePublicApi(typeof(RemediationProposal).Assembly);
        var expected = File.ReadAllText(GetBaselinePath()).TrimEnd();

        Assert.Equal(expected, actual);
    }

    private static string GeneratePublicApi(Assembly assembly)
    {
        var sb = new StringBuilder();

        foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName))
        {
            sb.AppendLine(type.IsInterface ? GenerateInterfaceApi(type) : GenerateClassApi(type));
        }

        return sb.ToString().TrimEnd();
    }

    private static string GenerateInterfaceApi(Type type)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"interface {type.FullName}");
        foreach (var method in type.GetMethods().OrderBy(m => m.Name))
        {
            var parameters = string.Join(", ",
                method.GetParameters().Select(p => $"{FormatType(p.ParameterType)} {p.Name}"));
            sb.AppendLine($"  {FormatType(method.ReturnType)} {method.Name}({parameters})");
        }

        return sb.ToString();
    }

    private static string GenerateClassApi(Type type)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"class {type.FullName}");
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .OrderBy(p => p.Name))
        {
            var accessor = property.CanWrite ? "{ get; set; }" : "{ get; }";
            sb.AppendLine($"  {FormatType(property.PropertyType)} {property.Name} {accessor}");
        }

        return sb.ToString();
    }

    private static string FormatType(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var genericName = type.Name[..type.Name.IndexOf('`')];
        var typeArgs = string.Join(", ", type.GetGenericArguments().Select(FormatType));
        return $"{genericName}<{typeArgs}>";
    }

    private static string GetBaselinePath()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(PublicApiSnapshotTests).Assembly.Location)
            ?? AppContext.BaseDirectory;

        return Path.Combine(assemblyDir, "public-api-baseline.txt");
    }
}
