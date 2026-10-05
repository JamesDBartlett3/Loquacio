using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Loquacio.Services;

/// <summary>
/// Manages the history log for processed dictation segments.
/// Persists to a JSON file in AppData with automatic rotation (max 1000 entries).
/// </summary>
public sealed class HistoryService : IHistoryService, IAsyncDisposable
{
    private const int MaxHistoryEntries = 1000;
    private const string HistoryFileName = "history.json";

    private readonly string _historyFilePath;
    private readonly ILogger<HistoryService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<HistoryEntry> _history = [];
    private bool _disposed;

    public HistoryService(ILogger<HistoryService> logger)
    {
        _logger = logger;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appData, "Loquacio");
        Directory.CreateDirectory(appFolder);

        _historyFilePath = Path.Combine(appFolder, HistoryFileName);
    }

    /// <summary>
    /// Loads history from disk on service initialization.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (File.Exists(_historyFilePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_historyFilePath);
                    _history = JsonSerializer.Deserialize<List<HistoryEntry>>(json) ?? [];
                    _logger.LogInformation("Loaded {Count} history entries from disk", _history.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load history file, starting with empty history");
                    _history = [];
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddEntryAsync(HistoryEntry entry)
    {
        await _lock.WaitAsync();
        try
        {
            _history.Insert(0, entry); // Most recent first

            // Rotate if over limit
            if (_history.Count > MaxHistoryEntries)
            {
                var toRemove = _history.Count - MaxHistoryEntries;
                _history.RemoveRange(MaxHistoryEntries, toRemove);
                _logger.LogDebug("Rotated {Count} old history entries", toRemove);
            }

            await SaveHistoryAsync();
            _logger.LogDebug("Added history entry: {Id}", entry.Id);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<HistoryEntry>> GetHistoryAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return [.. _history]; // Return a copy
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<HistoryEntry>> GetRecentHistoryAsync(int count)
    {
        await _lock.WaitAsync();
        try
        {
            var toTake = Math.Min(count, _history.Count);
            return _history.Take(toTake).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearHistoryAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _history.Clear();
            await SaveHistoryAsync();
            _logger.LogInformation("Cleared all history entries");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteEntryAsync(Guid id)
    {
        await _lock.WaitAsync();
        try
        {
            var entry = _history.FirstOrDefault(e => e.Id == id);
            if (entry != null)
            {
                _history.Remove(entry);
                await SaveHistoryAsync();
                _logger.LogDebug("Deleted history entry: {Id}", id);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> GetCountAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return _history.Count;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveHistoryAsync()
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var json = JsonSerializer.Serialize(_history, options);
            await File.WriteAllTextAsync(_historyFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save history file");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _lock.WaitAsync();
        try
        {
            await SaveHistoryAsync();
        }
        finally
        {
            _lock.Release();
        }

        _lock.Dispose();
    }
}