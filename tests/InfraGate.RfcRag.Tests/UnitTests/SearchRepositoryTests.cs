using Dapper;
using InfraGate.RfcRag.Infrastructure;
using InfraGate.RfcRag.Models;
using InfraGate.RfcRag.Search;
using Npgsql;
using Testcontainers.PostgreSql;

namespace InfraGate.RfcRag.Tests.UnitTests;

public sealed class SearchRepositoryTests : IAsyncLifetime
{
    private const string PostgresImage = "pgvector/pgvector:pg17";
    private const int RfcNumber = 9999;

    private PostgreSqlContainer? container;
    private NpgsqlDataSource? dataSource;

    public async ValueTask InitializeAsync()
    {
        container = new PostgreSqlBuilder(PostgresImage)
            .Build();

        await container.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        dataSource = NpgsqlDataSource.Create(container.GetConnectionString());
        await RfcRagMigrationRunner.ApplyAsync(dataSource, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (dataSource is not null)
        {
            await dataSource.DisposeAsync().ConfigureAwait(false);
        }

        if (container is not null)
        {
            await container.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task GetSectionWithChildrenAsync_DepthOne_ReturnsOnlyImmediateChildrenInNumericOrder()
    {
        await InsertSectionsAsync(
            "4.4",
            "4.4.10",
            "4.4.1.1",
            "4.4.2",
            "4.4.1",
            "4.4.2.1");
        var repository = CreateRepository();

        (_, IReadOnlyList<RfcSection> children) = await repository.GetSectionWithChildrenAsync(
            RfcNumber,
            "4.4",
            depth: 1,
            TestContext.Current.CancellationToken);

        Assert.Equal(["4.4.1", "4.4.2", "4.4.10"], children.Select(child => child.Section).ToArray());
    }

    [Fact]
    public async Task GetRfcAsync_SectionsWithDoubleDigitNumbers_ReturnsNumericOrder()
    {
        await InsertSectionsAsync("1", "10", "2", "3");
        var repository = CreateRepository();

        IReadOnlyList<RfcSection> sections = await repository.GetRfcAsync(
            RfcNumber,
            TestContext.Current.CancellationToken);

        Assert.Equal(["1", "2", "3", "10"], sections.Select(section => section.Section).ToArray());
    }

    [Fact]
    public async Task GetTocAsync_SectionsWithDoubleDigitNumbers_ReturnsNumericOrder()
    {
        await InsertSectionsAsync("1", "10", "2", "3");
        var repository = CreateRepository();

        IReadOnlyDictionary<string, string?> toc = await repository.GetTocAsync(
            RfcNumber,
            TestContext.Current.CancellationToken);

        Assert.Equal(["1", "2", "3", "10"], toc.Keys.ToArray());
    }

    [Fact]
    public async Task FindSectionsByHeadingsAsync_DuplicateHeadings_ReturnsFirstMatchWithoutThrowing()
    {
        await InsertSectionsAsync(("1", "Shared Heading"), ("2", "Shared Heading"));
        var repository = CreateRepository();

        IReadOnlyDictionary<string, RfcSection> sections = await repository.FindSectionsByHeadingsAsync(
            RfcNumber,
            ["Shared Heading"],
            TestContext.Current.CancellationToken);

        RfcSection section = Assert.Single(sections).Value;
        Assert.Equal("Shared Heading", section.Heading);
    }

    private SearchRepository CreateRepository() => new(dataSource!);

    private async Task InsertSectionsAsync(params string[] sections)
    {
        (string Section, string Heading)[] rows = sections
            .Select(section => (section, $"Section {section}"))
            .ToArray();

        await InsertSectionsAsync(rows).ConfigureAwait(false);
    }

    private async Task InsertSectionsAsync(params (string Section, string Heading)[] sections)
    {
        await using var connection = await dataSource!.OpenConnectionAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into rfc_rag.rfc_sections
                (rfc_number, title, section, heading, text, source_path, url, source_sha256)
            values
                (@RfcNumber, @Title, @Section, @Heading, @Text, @SourcePath, @Url, @SourceSha256)
            """,
            sections.Select(section => new
            {
                RfcNumber,
                Title = "Test RFC",
                section.Section,
                section.Heading,
                Text = $"Text for section {section.Section}",
                SourcePath = "rfc9999.txt",
                Url = "https://www.rfc-editor.org/rfc/rfc9999",
                SourceSha256 = "test-sha"
            }),
            cancellationToken: TestContext.Current.CancellationToken)).ConfigureAwait(false);
    }
}
