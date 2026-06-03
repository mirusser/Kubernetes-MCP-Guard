namespace InfraGate.AgentGuardrails.Tests.UnitTests;

public sealed class ModelVisibleContentOptionsTests
{
    [Fact]
    public void SectionName_IsPinned()
    {
        Assert.Equal("InfraGate:AgentGuardrails:ModelVisibleContent", ModelVisibleContentOptions.SectionName);
    }

    [Fact]
    public void Validate_Defaults_DoesNotThrow()
    {
        var options = new ModelVisibleContentOptions();

        var exception = Record.Exception(options.Validate);

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_SemanticClassifierEnabled_ThrowsUntilLocalAdapterExists()
    {
        var options = new ModelVisibleContentOptions
        {
            SemanticClassifierEnabled = true,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Validate_InvalidUnavailableBehavior_Throws()
    {
        var options = new ModelVisibleContentOptions
        {
            UnavailableBehavior = (ModelVisibleContentUnavailableBehavior)999,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Validate_EmptyQuarantinePlaceholder_Throws()
    {
        var options = new ModelVisibleContentOptions
        {
            QuarantinePlaceholder = string.Empty,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Validate_RequestTimeoutMillisecondsZero_Throws()
    {
        var options = new ModelVisibleContentOptions
        {
            RequestTimeoutMilliseconds = 0,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Validate_MaximumInputCharactersZero_Throws()
    {
        var options = new ModelVisibleContentOptions
        {
            MaximumInputCharacters = 0,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Validate_InvalidLocalClassifierBaseUrl_Throws()
    {
        var options = new ModelVisibleContentOptions
        {
            LocalClassifierBaseUrl = "not-an-absolute-uri",
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
