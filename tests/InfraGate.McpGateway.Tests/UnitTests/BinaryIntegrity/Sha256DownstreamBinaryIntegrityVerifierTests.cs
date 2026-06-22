using InfraGate.McpGateway.BinaryIntegrity;

namespace InfraGate.McpGateway.Tests.UnitTests.BinaryIntegrity;

public sealed class Sha256DownstreamBinaryIntegrityVerifierTests : IDisposable
{
    private readonly List<string> tempFiles = [];

    public void Dispose()
    {
        foreach (string file in tempFiles)
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void Verify_KnownBytes_ReturnsWithoutThrowing()
    {
        string path = CreateTempFile("hello world"u8.ToArray());

        var verifier = new Sha256DownstreamBinaryIntegrityVerifier();

        verifier.Verify(path, "b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9");
    }

    [Fact]
    public void Verify_KnownBytesUpperCaseHash_ReturnsWithoutThrowing()
    {
        string path = CreateTempFile("hello world"u8.ToArray());

        var verifier = new Sha256DownstreamBinaryIntegrityVerifier();

        verifier.Verify(path, "B94D27B9934D3E08A52E52D7DA7DABFAC484EFE37A5380EE9088F7ACE2EFCDE9");
    }

    [Fact]
    public void Verify_MismatchedHash_ThrowsDownstreamBinaryIntegrityException()
    {
        string path = CreateTempFile("content"u8.ToArray());

        var verifier = new Sha256DownstreamBinaryIntegrityVerifier();
        DownstreamBinaryIntegrityException exception = Assert.Throws<DownstreamBinaryIntegrityException>(
            () => verifier.Verify(path, new string('0', 64)));

        Assert.Equal(path, exception.FilePath);
        Assert.Equal(Sha256DownstreamBinaryIntegrityVerifier.AlgorithmName, exception.Algorithm);
    }

    [Fact]
    public void Verify_MissingFile_ThrowsDownstreamBinaryIntegrityException()
    {
        string path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.dll");

        var verifier = new Sha256DownstreamBinaryIntegrityVerifier();
        DownstreamBinaryIntegrityException exception = Assert.Throws<DownstreamBinaryIntegrityException>(
            () => verifier.Verify(path, new string('0', 64)));

        Assert.Equal(path, exception.FilePath);
        Assert.Equal(Sha256DownstreamBinaryIntegrityVerifier.AlgorithmName, exception.Algorithm);
    }

    [Fact]
    public void Verify_MalformedHex_ThrowsFormatException()
    {
        string path = CreateTempFile("content"u8.ToArray());

        var verifier = new Sha256DownstreamBinaryIntegrityVerifier();

        Assert.Throws<FormatException>(() => verifier.Verify(path, new string('g', 64)));
    }

    [Fact]
    public void Verify_EmptyHex_ThrowsFormatException()
    {
        string path = CreateTempFile("content"u8.ToArray());

        var verifier = new Sha256DownstreamBinaryIntegrityVerifier();

        Assert.Throws<FormatException>(() => verifier.Verify(path, string.Empty));
    }

    [Theory]
    [InlineData(62)]
    [InlineData(66)]
    public void Verify_WrongLengthHex_ThrowsFormatException(int length)
    {
        string path = CreateTempFile("content"u8.ToArray());

        var verifier = new Sha256DownstreamBinaryIntegrityVerifier();

        Assert.Throws<FormatException>(() => verifier.Verify(path, new string('0', length)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Verify_EmptyOrWhitespaceAssemblyPath_ThrowsArgumentException(string assemblyPath)
    {
        var verifier = new Sha256DownstreamBinaryIntegrityVerifier();

        Assert.Throws<ArgumentException>(() => verifier.Verify(assemblyPath, new string('0', 64)));
    }

    [Fact]
    public void Verify_NullAssemblyPath_ThrowsArgumentNullException()
    {
        var verifier = new Sha256DownstreamBinaryIntegrityVerifier();

        Assert.Throws<ArgumentNullException>(() => verifier.Verify(null!, new string('0', 64)));
    }

    private string CreateTempFile(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), $"integrity-test-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, bytes);
        tempFiles.Add(path);
        return path;
    }
}
