using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Loquacio.Ipc;
using Loquacio.Models;
using Loquacio.Services;
using Microsoft.Extensions.Logging;

namespace Loquacio.Avalonia.ViewModels;

/// <summary>
/// Phase 6.4 — History log viewer ViewModel.
/// Loads persisted dictation history from the engine over IPC
/// (get-history), newest first, with copy-selected and clear-all.
/// </summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly IEngineProxy _engine;
    private readonly IClipboardService _clipboard;
    private readonly ILogger<HistoryViewModel> _logger;

    /// <summary>Parses the JSON payload returned by get-history. Internal for tests.</summary>
    public static List<HistoryEntry>? ParseHistoryPayload(string? payload) =>
        payload is null ? null : JsonSerializer.Deserialize<List<HistoryEntry>>(payload);

    public HistoryViewModel(IEngineProxy engine, IClipboardService clipboard, ILogger<HistoryViewModel> logger)
    {
        _engine = engine;
        _clipboard = clipboard;
        _logger = logger;
    }

    public ObservableCollection<HistoryEntry> Entries { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EntryCountText))]
    private int _entryCount;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private HistoryEntry? _selectedEntry;

    public string EntryCountText => $"{EntryCount} entr{(EntryCount == 1 ? "y" : "ies")}";

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var ack = await _engine.SendCommandAsync(new GetHistoryMessage { Limit = 500 });
            if (!ack.Success)
            {
                _logger.LogWarning("get-history failed: {Error}", ack.Error);
                return;
            }
            var entries = ParseHistoryPayload(ack.Error);
            Entries.Clear();
            if (entries is not null)
            {
                foreach (var e in entries)
                    Entries.Add(e);
            }
            EntryCount = Entries.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load history");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CopySelectedAsync()
    {
        if (SelectedEntry is null) return;
        try
        {
            await _clipboard.CopyToClipboardAsync(SelectedEntry.DisplayText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy history entry");
        }
    }

    [RelayCommand]
    private async Task ClearAllAsync()
    {
        try
        {
            var ack = await _engine.SendCommandAsync(new ClearHistoryMessage());
            if (ack.Success)
            {
                Entries.Clear();
                EntryCount = 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear history");
        }
    }
}
