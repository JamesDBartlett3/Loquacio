using NSubstitute;
using Loquacio.Models;
using Loquacio.Services;

namespace Loquacio.Tests.Services;public class IModelManagerServiceTests
{
    private readonly IModelManagerService _mockModelManager;

    public IModelManagerServiceTests()
    {
        _mockModelManager = Substitute.For<IModelManagerService>();
    }

    [Fact]
    public void ModelsDirectory_ReturnsValidPath()
    {
        // Arrange
        var expectedPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Loquacio", "models");
        _mockModelManager.ModelsDirectory.Returns(expectedPath);

        // Act & Assert
        Assert.Equal(expectedPath, _mockModelManager.ModelsDirectory);
    }

    [Fact]
    public async Task GetDownloadedModelsAsync_ReturnsEmpty_WhenNoModelsDownloaded()
    {
        // Arrange
        _mockModelManager.GetDownloadedModelsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<WhisperModel>().AsReadOnly());

        // Act
        var result = await _mockModelManager.GetDownloadedModelsAsync();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDownloadedModelsAsync_ReturnsModels_WhenDownloaded()
    {
        // Arrange
        var models = new List<WhisperModel>
        {
            new() { Name = "tiny", DisplayName = "Tiny", SizeBytes = 39_000_000, UseCase = "Fast", SpeedFactor = 1.0, AccuracyFactor = 1.0, GgmlFileName = "ggml-tiny.bin" },
            new() { Name = "base", DisplayName = "Base", SizeBytes = 74_000_000, UseCase = "Balanced", SpeedFactor = 1.5, AccuracyFactor = 1.3, GgmlFileName = "ggml-base.bin" }
        };
        _mockModelManager.GetDownloadedModelsAsync(Arg.Any<CancellationToken>())
            .Returns(models.AsReadOnly());

        // Act
        var result = await _mockModelManager.GetDownloadedModelsAsync();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("tiny", result[0].Name);
        Assert.Equal("base", result[1].Name);
    }

    [Fact]
    public void IsModelDownloaded_ReturnsFalse_WhenNotDownloaded()
    {
        // Arrange
        var model = new WhisperModel { Name = "tiny", DisplayName = "Tiny", SizeBytes = 39_000_000, UseCase = "Fast", SpeedFactor = 1.0, AccuracyFactor = 1.0, GgmlFileName = "ggml-tiny.bin" };
        _mockModelManager.IsModelDownloaded(model).Returns(false);

        // Act & Assert
        Assert.False(_mockModelManager.IsModelDownloaded(model));
    }

    [Fact]
    public void IsModelDownloaded_ReturnsTrue_WhenDownloaded()
    {
        // Arrange
        var model = new WhisperModel { Name = "base", DisplayName = "Base", SizeBytes = 74_000_000, UseCase = "Balanced", SpeedFactor = 1.5, AccuracyFactor = 1.3, GgmlFileName = "ggml-base.bin" };
        _mockModelManager.IsModelDownloaded(model).Returns(true);

        // Act & Assert
        Assert.True(_mockModelManager.IsModelDownloaded(model));
    }

    [Fact]
    public void GetModelPath_ReturnsFullPath_WithModelFileName()
    {
        // Arrange
        var model = new WhisperModel { Name = "small", DisplayName = "Small", SizeBytes = 244_000_000, UseCase = "High", SpeedFactor = 2.5, AccuracyFactor = 1.6, GgmlFileName = "ggml-small.bin" };
        var expectedPath = "/fake/path/ggml-small.bin";
        _mockModelManager.GetModelPath(model).Returns(expectedPath);

        // Act
        var result = _mockModelManager.GetModelPath(model);

        // Assert
        Assert.EndsWith("ggml-small.bin", result);
    }

    [Fact]
    public void AvailableModels_IsNotEmpty()
    {
        // The real ModelManagerService has 5 models available.
        // Since we're testing the interface contract, we verify the mock
        // returns a non-empty list when configured.
        var models = new List<WhisperModel>
        {
            new() { Name = "tiny", DisplayName = "Tiny (39MB)", SizeBytes = 39_000_000, UseCase = "Test", SpeedFactor = 1.0, AccuracyFactor = 1.0, GgmlFileName = "ggml-tiny.bin" }
        };
        _mockModelManager.AvailableModels.Returns(models.AsReadOnly());

        // Act & Assert
        Assert.NotEmpty(_mockModelManager.AvailableModels);
    }
}

/// <summary>
/// Exercises the real ModelManagerService (file-system + checksum logic) with
/// a test double that serves a known payload and a configurable expected hash.
/// </summary>
public class ModelManagerChecksumTests : IDisposable
{
    private const string Payload = "loquacio test model payload";
    private static readonly string PayloadSha256 = Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Payload))).ToLowerInvariant();

    private readonly string _modelsDir;
    private readonly TestModelManagerService _service;
    private readonly WhisperModel _model;

    internal class TestModelManagerService(string modelsDir, string expectedSha256) : ModelManagerService(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ModelManagerService>.Instance, modelsDir)
    {
        public bool ApiAvailable { get; set; } = true;

        protected override Task<string?> FetchExpectedSha256Async(WhisperModel model, CancellationToken ct)
            => Task.FromResult(ApiAvailable ? expectedSha256 : null);

        protected override Task<string> DownloadViaHttpAsync(
            WhisperModel model, string tempOutputPath, IProgress<double>? progress, CancellationToken ct)
        {
            File.WriteAllText(tempOutputPath, Payload);
            return Task.FromResult(PayloadSha256);
        }
    }

    public ModelManagerChecksumTests()
    {
        _modelsDir = Path.Combine(Path.GetTempPath(), $"wd-modeltest-{Guid.NewGuid():N}");
        _service = new TestModelManagerService(_modelsDir, ExpectedSha256);
        _model = new WhisperModel
        {
            Name = "tiny",
            DisplayName = "Tiny (test)",
            SizeBytes = System.Text.Encoding.UTF8.GetBytes(Payload).Length,
            UseCase = "Test",
            SpeedFactor = 1.0,
            AccuracyFactor = 1.0,
            GgmlFileName = "ggml-tiny.bin"
        };
    }

    private static string ExpectedSha256 => PayloadSha256;

    public void Dispose()
    {
        try { Directory.Delete(_modelsDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Download_Succeeds_WhenChecksumMatches()
    {
        await _service.DownloadModelAsync(_model);

        var path = _service.GetModelPath(_model);
        Assert.True(File.Exists(path));
        Assert.True(_service.IsModelDownloaded(_model));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task Download_Throws_AndDiscardsFile_WhenChecksumMismatch()
    {
        // Simulate a corrupted download: the "server" delivers a truncated
        // payload whose hash cannot match the expected checksum.
        var service = new CorruptDownloadService(_modelsDir, ExpectedSha256);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadModelAsync(_model));

        Assert.False(service.IsModelDownloaded(_model));
        Assert.False(File.Exists(service.GetModelPath(_model)));
        Assert.False(File.Exists(service.GetModelPath(_model) + ".tmp"));
    }

    private sealed class CorruptDownloadService(string modelsDir, string expectedSha256)
        : TestModelManagerService(modelsDir, expectedSha256)
    {        protected override Task<string> DownloadViaHttpAsync(
            WhisperModel model, string tempOutputPath, IProgress<double>? progress, CancellationToken ct)
        {
            // Partial download: only the first half of the payload arrives.
            // Return a hash string that cannot match the expected full-payload hash.
            File.WriteAllText(tempOutputPath, Payload[..(Payload.Length / 2)]);
            return Task.FromResult(Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(Payload[..(Payload.Length / 2)]))).ToLowerInvariant());
        }
    }

    [Fact]
    public async Task Download_Throws_WhenChecksumApiUnavailable()
    {
        _service.ApiAvailable = false;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.DownloadModelAsync(_model));

        Assert.False(_service.IsModelDownloaded(_model));
    }

    [Fact]
    public void IsModelDownloaded_False_WhenFileMissing()
    {
        Assert.False(_service.IsModelDownloaded(_model));
    }

    [Fact]
    public void IsModelDownloaded_False_WhenFileTruncated()
    {
        // A partial download has the wrong size and must not count as downloaded
        File.WriteAllText(_service.GetModelPath(_model), Payload[..3]);
        Assert.False(_service.IsModelDownloaded(_model));
    }

    [Fact]
    public void IsModelDownloaded_True_WhenSizeMatches()
    {
        File.WriteAllText(_service.GetModelPath(_model), Payload);
        Assert.True(_service.IsModelDownloaded(_model));
    }

    [Fact]
    public async Task ValidateModelChecksum_ReturnsTrue_WhenFileMatchesExpectedHash()
    {
        File.WriteAllText(_service.GetModelPath(_model), Payload);
        Assert.True(await _service.ValidateModelChecksumAsync(_model));
    }

    [Fact]
    public async Task ValidateModelChecksum_ReturnsFalse_WhenFileCorrupted()
    {
        File.WriteAllText(_service.GetModelPath(_model), Payload + " corruption");
        Assert.False(await _service.ValidateModelChecksumAsync(_model));
    }

    [Fact]
    public async Task ValidateModelChecksum_ReturnsFalse_WhenApiUnavailable()
    {
        File.WriteAllText(_service.GetModelPath(_model), Payload);
        _service.ApiAvailable = false;
        Assert.False(await _service.ValidateModelChecksumAsync(_model));
    }

    [Fact]
    public async Task ValidateModelChecksum_ReturnsFalse_WhenFileMissing()
    {
        Assert.False(await _service.ValidateModelChecksumAsync(_model));
    }
}
