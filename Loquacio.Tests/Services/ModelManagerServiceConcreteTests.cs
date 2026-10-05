using Microsoft.Extensions.Logging.Abstractions;
using Loquacio.Services;

namespace Loquacio.Tests.Services;

/// <summary>
/// Tests for the concrete ModelManagerService implementation using a temp directory.
/// </summary>
public class ModelManagerServiceConcreteTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ModelManagerService _service;

    public ModelManagerServiceConcreteTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LoquacioTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _service = new ModelManagerService(NullLogger<ModelManagerService>.Instance, _tempDir);
    }

    [Fact]
    public void AvailableModels_ContainsExpectedSizes()
    {
        var names = _service.AvailableModels.Select(m => m.Name).ToList();
        Assert.Contains("tiny", names);
        Assert.Contains("base", names);
        Assert.Contains("small", names);
        Assert.Contains("medium", names);
        Assert.Contains("large-v3", names);
    }

    [Fact]
    public void IsModelDownloaded_ReturnsFalse_WhenFileAbsent()
    {
        var model = _service.AvailableModels.First(m => m.Name == "tiny");
        Assert.False(_service.IsModelDownloaded(model));
    }

    [Fact]
    public void IsModelDownloaded_ReturnsTrue_WhenFilePresent()
    {
        var model = _service.AvailableModels.First(m => m.Name == "tiny");
        var path = _service.GetModelPath(model);

        // Create a fake model file with the exact expected size
        // (a file whose size differs counts as a partial download)
        using (var fs = new FileStream(path, FileMode.Create)) { fs.SetLength(model.SizeBytes); }

        Assert.True(_service.IsModelDownloaded(model));
    }

    [Fact]
    public void GetModelPath_ReturnsPathInModelsDirectory()
    {
        var model = _service.AvailableModels.First(m => m.Name == "base");
        var path = _service.GetModelPath(model);
        Assert.StartsWith(_tempDir, path);
        Assert.EndsWith(model.GgmlFileName, path);
    }

    [Fact]
    public async Task GetDownloadedModelsAsync_ReturnsOnlyExistingFiles()
    {
        // Create fake file for "tiny" only, with the exact expected size
        var tiny = _service.AvailableModels.First(m => m.Name == "tiny");
        using (var fs = new FileStream(_service.GetModelPath(tiny), FileMode.Create)) { fs.SetLength(tiny.SizeBytes); }

        var downloaded = await _service.GetDownloadedModelsAsync();

        Assert.Single(downloaded);
        Assert.Equal("tiny", downloaded[0].Name);
    }

    [Fact]
    public async Task DeleteModelAsync_RemovesFile()
    {
        var model = _service.AvailableModels.First(m => m.Name == "tiny");
        var path = _service.GetModelPath(model);
        File.WriteAllBytes(path, new byte[100]);

        Assert.True(File.Exists(path));

        await _service.DeleteModelAsync(model);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ModelsDirectory_UsesInjectedPath()
    {
        Assert.Equal(_tempDir, _service.ModelsDirectory);
    }

    public void Dispose()
    {
        _service.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
