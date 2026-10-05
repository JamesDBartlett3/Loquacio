using NSubstitute;
using Loquacio.Models;
using Loquacio.Services;

namespace Loquacio.Tests;

/// <summary>
/// Tests for the VocabularyService — custom dictionary management.
/// </summary>
public class VocabularyServiceTests
{
    private ISettingsService CreateMockSettingsService(Settings? initialSettings = null)
    {
        var settings = initialSettings ?? new Settings();
        var mock = Substitute.For<ISettingsService>();

        var currentSettings = settings;
        mock.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(currentSettings));

        mock.When(s => s.SaveSettingsAsync(Arg.Any<Settings>(), Arg.Any<CancellationToken>()))
            .Do(c => currentSettings = c.Arg<Settings>());

        return mock;
    }

    [Fact]
    public async Task GetWordsAsync_EmptyByDefault()
    {
        var settingsMock = CreateMockSettingsService();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        var words = await service.GetWordsAsync();

        Assert.Empty(words);
    }

    [Fact]
    public async Task GetWordsAsync_LoadsFromSettings()
    {
        var settings = new Settings
        {
            Vocabulary = new VocabularySettings
            {
                CustomWords = new List<string> { "Power BI", "DAX", "Fabric" }
            }
        };
        var settingsMock = CreateMockSettingsService(settings);
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        var words = await service.GetWordsAsync();

        Assert.Equal(3, words.Count);
        Assert.Contains("Power BI", words);
        Assert.Contains("DAX", words);
        Assert.Contains("Fabric", words);
    }

    [Fact]
    public async Task AddWordAsync_AddsWord()
    {
        var settingsMock = CreateMockSettingsService();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        var added = await service.AddWordAsync("test");

        Assert.True(added);
        var words = await service.GetWordsAsync();
        Assert.Contains("test", words);
    }

    [Fact]
    public async Task AddWordAsync_DoesNotAddDuplicate()
    {
        var settingsMock = CreateMockSettingsService();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        await service.AddWordAsync("test");
        var added = await service.AddWordAsync("test");

        Assert.False(added);
        var words = await service.GetWordsAsync();
        Assert.Single(words);
    }

    [Fact]
    public async Task AddWordAsync_DoesNotAddDuplicate_CaseInsensitive()
    {
        var settingsMock = CreateMockSettingsService();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        await service.AddWordAsync("Test");
        var added = await service.AddWordAsync("TEST");

        Assert.False(added);
        var words = await service.GetWordsAsync();
        Assert.Single(words);
    }

    [Fact]
    public async Task AddWordAsync_RejectsEmptyString()
    {
        var settingsMock = CreateMockSettingsService();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        var added = await service.AddWordAsync("");

        Assert.False(added);
    }

    [Fact]
    public async Task AddWordAsync_RejectsWhitespace()
    {
        var settingsMock = CreateMockSettingsService();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        var added = await service.AddWordAsync("   ");

        Assert.False(added);
    }

    [Fact]
    public async Task AddWordAsync_TrimsWhitespace()
    {
        var settingsMock = CreateMockSettingsService();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        await service.AddWordAsync("  test  ");

        var words = await service.GetWordsAsync();
        Assert.Contains("test", words);
        Assert.DoesNotContain("  test  ", words);
    }

    [Fact]
    public async Task AddWordAsync_RejectsTooLongWord()
    {
        var settingsMock = CreateMockSettingsService();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        var longWord = new string('a', 201);
        var added = await service.AddWordAsync(longWord);

        Assert.False(added);
    }

    [Fact]
    public async Task RemoveWordAsync_RemovesExistingWord()
    {
        var settings = new Settings
        {
            Vocabulary = new VocabularySettings
            {
                CustomWords = new List<string> { "alpha", "beta", "gamma" }
            }
        };
        var settingsMock = CreateMockSettingsService(settings);
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        var removed = await service.RemoveWordAsync("beta");

        Assert.True(removed);
        var words = await service.GetWordsAsync();
        Assert.DoesNotContain("beta", words);
        Assert.Equal(2, words.Count);
    }

    [Fact]
    public async Task RemoveWordAsync_ReturnsFalseForMissingWord()
    {
        var settingsMock = CreateMockSettingsService();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        var removed = await service.RemoveWordAsync("nonexistent");

        Assert.False(removed);
    }

    [Fact]
    public async Task ContainsWordAsync_ReturnsTrueForExisting()
    {
        var settings = new Settings
        {
            Vocabulary = new VocabularySettings
            {
                CustomWords = new List<string> { "test" }
            }
        };
        var settingsMock = CreateMockSettingsService(settings);
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        var exists = await service.ContainsWordAsync("test");

        Assert.True(exists);
    }

    [Fact]
    public async Task ContainsWordAsync_ReturnsFalseForMissing()
    {
        var settingsMock = CreateMockSettingsService();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        var exists = await service.ContainsWordAsync("nonexistent");

        Assert.False(exists);
    }

    [Fact]
    public async Task ContainsWordAsync_CaseInsensitive()
    {
        var settings = new Settings
        {
            Vocabulary = new VocabularySettings
            {
                CustomWords = new List<string> { "Test" }
            }
        };
        var settingsMock = CreateMockSettingsService(settings);
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        var exists = await service.ContainsWordAsync("TEST");

        Assert.True(exists);
    }

    [Fact]
    public async Task ImportWordsAsync_MergesWithExisting()
    {
        var settings = new Settings
        {
            Vocabulary = new VocabularySettings
            {
                CustomWords = new List<string> { "alpha" }
            }
        };
        var settingsMock = CreateMockSettingsService(settings);
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        await service.ImportWordsAsync(new[] { "beta", "gamma", "alpha" }); // alpha is a dup

        var words = await service.GetWordsAsync();
        Assert.Equal(3, words.Count);
        Assert.Contains("alpha", words);
        Assert.Contains("beta", words);
        Assert.Contains("gamma", words);
    }

    [Fact]
    public async Task ImportWordsAsync_FiltersEmptyAndWhitespace()
    {
        var settingsMock = CreateMockSettingsService();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        await service.ImportWordsAsync(new[] { "valid", "", "   ", "also-valid" });

        var words = await service.GetWordsAsync();
        Assert.Equal(2, words.Count);
        Assert.Contains("valid", words);
        Assert.Contains("also-valid", words);
    }

    [Fact]
    public async Task ClearAsync_RemovesAllWords()
    {
        var settings = new Settings
        {
            Vocabulary = new VocabularySettings
            {
                CustomWords = new List<string> { "a", "b", "c" }
            }
        };
        var settingsMock = CreateMockSettingsService(settings);
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        await service.ClearAsync();

        var words = await service.GetWordsAsync();
        Assert.Empty(words);
    }

    [Fact]
    public async Task GetContextStringAsync_ReturnsCommaSeparated()
    {
        var settings = new Settings
        {
            Vocabulary = new VocabularySettings
            {
                CustomWords = new List<string> { "alpha", "beta", "gamma" }
            }
        };
        var settingsMock = CreateMockSettingsService(settings);
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        var context = await service.GetContextStringAsync();

        Assert.Equal("alpha, beta, gamma", context);
    }

    [Fact]
    public async Task GetContextStringAsync_EmptyWhenNoWords()
    {
        var settingsMock = CreateMockSettingsService();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        var context = await service.GetContextStringAsync();

        Assert.Equal(string.Empty, context);
    }

    [Fact]
    public async Task VocabularyChanged_FiresOnAdd()
    {
        var settingsMock = CreateMockSettingsService();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        var eventFired = false;
        service.VocabularyChanged += (s, e) => eventFired = true;

        await service.AddWordAsync("test");

        Assert.True(eventFired);
    }

    [Fact]
    public async Task VocabularyChanged_FiresOnRemove()
    {
        var settings = new Settings
        {
            Vocabulary = new VocabularySettings
            {
                CustomWords = new List<string> { "test" }
            }
        };
        var settingsMock = CreateMockSettingsService(settings);
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        var eventFired = false;
        service.VocabularyChanged += (s, e) => eventFired = true;

        await service.RemoveWordAsync("test");

        Assert.True(eventFired);
    }

    [Fact]
    public async Task VocabularyChanged_FiresOnClear()
    {
        var settings = new Settings
        {
            Vocabulary = new VocabularySettings
            {
                CustomWords = new List<string> { "test" }
            }
        };
        var settingsMock = CreateMockSettingsService(settings);
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        var eventFired = false;
        service.VocabularyChanged += (s, e) => eventFired = true;

        await service.ClearAsync();

        Assert.True(eventFired);
    }

    [Fact]
    public async Task AddRemove_PersistsToSettings()
    {
        var savedSettings = (Settings?)null;
        var settingsMock = Substitute.For<ISettingsService>();
        var currentSettings = new Settings();
        settingsMock.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(currentSettings));
        settingsMock.When(s => s.SaveSettingsAsync(Arg.Any<Settings>(), Arg.Any<CancellationToken>()))
            .Do(c => savedSettings = c.Arg<Settings>());

        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VocabularyService>>();
        var service = new VocabularyService(settingsMock, logger);

        await service.AddWordAsync("persisted-word");

        Assert.NotNull(savedSettings);
        Assert.Contains("persisted-word", savedSettings!.Vocabulary.CustomWords);
    }
}
