using System.Reflection;
using System.Text;

namespace InfraGate.Observer.Contracts.Tests.UnitTests;

public sealed class PublicApiSnapshotTests
{
    [Fact]
    public void PublicApiMatchesCommittedBaseline()
    {
        var actual = GeneratePublicApi(typeof(AnomalyReport).Assembly);
        var expected = File.ReadAllText(GetBaselinePath());

        Assert.Equal(expected, actual);
    }

    private static string GeneratePublicApi(Assembly assembly)
    {
        var sb = new StringBuilder();

        foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName))
        {
            sb.AppendLine(type.IsEnum switch
            {
                true => GenerateEnumApi(type),
                false when type.IsInterface => GenerateInterfaceApi(type),
                _ => GenerateClassApi(type)
            });
        }

        return sb.ToString().TrimEnd();
    }

    private static string GenerateEnumApi(Type type)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"enum {type.FullName}");
        foreach (var name in Enum.GetNames(type).OrderBy(n => n))
        {
            sb.AppendLine($"  {name}");
        }

        return sb.ToString();
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

        if (type.IsAbstract && type.IsSealed)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static)
                         .OrderBy(f => f.Name))
            {
                sb.AppendLine($"  const {FormatType(field.FieldType)} {field.Name}");
            }
        }
        else
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .OrderBy(p => p.Name))
            {
                var accessor = property.CanWrite ? "{ get; set; }" : "{ get; }";
                sb.AppendLine($"  {FormatType(property.PropertyType)} {property.Name} {accessor}");
            }
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
