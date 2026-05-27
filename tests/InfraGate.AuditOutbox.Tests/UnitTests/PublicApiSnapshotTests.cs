using System.Reflection;
using System.Text;
using InfraGate.AuditOutbox.Postgres;

namespace InfraGate.AuditOutbox.Tests.UnitTests;

public sealed class PublicApiSnapshotTests
{
    [Fact]
    public void InfraGateAuditOutbox_PublicApiMatchesCommittedBaseline()
    {
        var actual = GeneratePublicApi(typeof(AuditOutboxRow).Assembly);
        var expected = File.ReadAllText(GetBaselinePath("audit-outbox-api-baseline.txt")).TrimEnd();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void InfraGateAuditOutboxPostgres_PublicApiMatchesCommittedBaseline()
    {
        var actual = GeneratePublicApi(typeof(PostgresAuditOutboxMigrationRunner).Assembly);
        var expected = File.ReadAllText(GetBaselinePath("audit-outbox-postgres-api-baseline.txt")).TrimEnd();
        Assert.Equal(expected, actual);
    }

    private static string GetBaselinePath(string fileName)
    {
        var assemblyDir = Path.GetDirectoryName(typeof(PublicApiSnapshotTests).Assembly.Location)
            ?? AppContext.BaseDirectory;

        return Path.Combine(assemblyDir, fileName);
    }

    private static string GeneratePublicApi(Assembly assembly)
    {
        var sb = new StringBuilder();

        foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName))
        {
            if (type.IsInterface)
            {
                sb.AppendLine($"interface {type.FullName}");
                foreach (var method in type.GetMethods().OrderBy(m => m.Name))
                {
                    var parameters = string.Join(", ",
                        method.GetParameters().Select(p => $"{FormatType(p.ParameterType)} {p.Name}"));
                    sb.AppendLine($"  {FormatType(method.ReturnType)} {method.Name}({parameters})");
                }
            }
            else if (type.IsValueType)
            {
                sb.AppendLine($"struct {type.FullName}");
                AppendMembers(sb, type);
            }
            else
            {
                var kind = type.IsAbstract && type.IsSealed ? "static class"
                    : type.IsGenericType ? $"class {type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(FormatType))}>"
                    : type is { IsAbstract: false, IsSealed: true } ? "record class"
                    : "class";
                sb.AppendLine($"{kind} {type.FullName}");
                AppendMembers(sb, type);
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendMembers(StringBuilder sb, Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                     .OrderBy(p => p.Name))
        {
            var accessor = property.CanWrite ? "{ get; set; }" : "{ get; }";
            var stat = property.GetMethod?.IsStatic == true ? "static " : "";
            sb.AppendLine($"  {stat}{FormatType(property.PropertyType)} {property.Name} {accessor}");
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .OrderBy(f => f.Name))
        {
            sb.AppendLine($"  const {FormatType(field.FieldType)} {field.Name}");
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .Where(m => !m.IsSpecialName)
                     .OrderBy(m => m.Name))
        {
            var parameters = string.Join(", ",
                method.GetParameters().Select(p => $"{FormatType(p.ParameterType)} {p.Name}"));
            var returnType = method.ReturnType == typeof(void) ? "void" : FormatType(method.ReturnType);
            sb.AppendLine($"  static {returnType} {method.Name}({parameters})");
        }
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
}
