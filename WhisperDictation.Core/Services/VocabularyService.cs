using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WhisperDictation.Models;

namespace WhisperDictation.Services;

/// <summary>
/// Thread-safe vocabulary service backed by settings persistence.
/// Stores custom words in the VocabularySettings section of settings.json.
/// </summary>
public class VocabularyService : IVocabularyService
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<VocabularyService> _logger;
    private readonly ConcurrentDictionary<string, string> _words = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private bool _initialized;

    public event EventHandler? VocabularyChanged;

    public VocabularyService(ISettingsService settingsService, ILogger<VocabularyService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _saveLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var settings = await _settingsService.GetSettingsAsync(ct);
            _words.Clear();
            foreach (var word in settings.Vocabulary.CustomWords)
            {
                if (!string.IsNullOrWhiteSpace(word))
                {
                    _words.TryAdd(word.Trim(), word.Trim());
                }
            }

            _initialized = true;
            _logger.LogDebug("Vocabulary loaded with {Count} words", _words.Count);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task<IReadOnlyList<string>> GetWordsAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _words.Keys.OrderBy(w => w).ToList();
    }

    public async Task<bool> AddWordAsync(string word, CancellationToken ct = default)
    {
        word = word?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(word))
        {
            return false;
        }

        // Reject words that are too long
        if (word.Length > 200)
        {
            _logger.LogWarning("Rejected vocabulary word (too long): {Word}", word.Substring(0, 50));
            return false;
        }

        await EnsureLoadedAsync(ct);

        if (!_words.TryAdd(word, word))
        {
            _logger.LogDebug("Word already in vocabulary: {Word}", word);
            return false;
        }

        await SaveAsync(ct);
        VocabularyChanged?.Invoke(this, EventArgs.Empty);

        _logger.LogInformation("Added vocabulary word: {Word}", word);
        return true;
    }

    public async Task<bool> RemoveWordAsync(string word, CancellationToken ct = default)
    {
        word = word?.Trim() ?? string.Empty;

        await EnsureLoadedAsync(ct);

        if (!_words.TryRemove(word, out _))
        {
            return false;
        }

        await SaveAsync(ct);
        VocabularyChanged?.Invoke(this, EventArgs.Empty);

        _logger.LogInformation("Removed vocabulary word: {Word}", word);
        return true;
    }

    public async Task<bool> ContainsWordAsync(string word, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _words.ContainsKey(word ?? string.Empty);
    }

    public async Task ImportWordsAsync(IEnumerable<string> words, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        var added = 0;
        foreach (var word in words)
        {
            var w = word?.Trim();
            if (!string.IsNullOrWhiteSpace(w) && w.Length <= 200)
            {
                if (_words.TryAdd(w, w))
                {
                    added++;
                }
            }
        }

        if (added > 0)
        {
            await SaveAsync(ct);
            VocabularyChanged?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation("Imported {Count} vocabulary words", added);
        }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        _words.Clear();
        await SaveAsync(ct);
        VocabularyChanged?.Invoke(this, EventArgs.Empty);
        _logger.LogInformation("Vocabulary cleared");
    }

    public async Task<string> GetContextStringAsync(CancellationToken ct = default)
    {
        var words = await GetWordsAsync(ct);

        if (words.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", words);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        await _saveLock.WaitAsync(ct);
        try
        {
            var settings = await _settingsService.GetSettingsAsync(ct);
            settings.Vocabulary.CustomWords = _words.Keys.OrderBy(w => w).ToList();
            await _settingsService.SaveSettingsAsync(settings, ct);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
