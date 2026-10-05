using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Loquacio.Models;
using Loquacio.Services;

namespace Loquacio.ViewModels;

public partial class VocabularyTabViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IVocabularyService _vocabularyService;

    [ObservableProperty]
    private string _newWord = string.Empty;

    [ObservableProperty]
    private int _wordCount;

    public ObservableCollection<string> CustomWords { get; } = new();

    /// <summary>Raised after settings are saved, so the host can push them to the daemon via IPC.</summary>
    public event EventHandler? SettingsSaved;

    public VocabularyTabViewModel(ISettingsService settingsService, IVocabularyService vocabularyService)
    {
        _settingsService = settingsService;
        _vocabularyService = vocabularyService;

        // Subscribe to vocabulary changes from other sources
        _vocabularyService.VocabularyChanged += OnVocabularyChanged;

        LoadAsync();
    }

    private async void LoadAsync()
    {
        var words = await _vocabularyService.GetWordsAsync();
        CustomWords.Clear();
        foreach (var word in words)
        {
            CustomWords.Add(word);
        }
        WordCount = CustomWords.Count;
    }

    private void OnVocabularyChanged(object? sender, EventArgs e)
    {
        // Reload from service when vocabulary changes externally
        LoadAsync();
    }

    [RelayCommand]
    private async Task AddWord()
    {
        var word = NewWord.Trim();
        if (string.IsNullOrEmpty(word)) return;

        var added = await _vocabularyService.AddWordAsync(word);
        if (added)
        {
            if (!CustomWords.Contains(word))
            {
                // Insert in sorted order
                var insertIndex = CustomWords.ToList().BinarySearch(word, StringComparer.OrdinalIgnoreCase);
                if (insertIndex < 0) insertIndex = ~insertIndex;
                CustomWords.Insert(insertIndex, word);
                WordCount = CustomWords.Count;
            }
            NewWord = string.Empty;
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private async Task RemoveWord(string word)
    {
        var removed = await _vocabularyService.RemoveWordAsync(word);
        if (removed)
        {
            CustomWords.Remove(word);
            WordCount = CustomWords.Count;
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private async Task ClearAll()
    {
        await _vocabularyService.ClearAsync();
        CustomWords.Clear();
        WordCount = 0;
        SettingsSaved?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyTo(VocabularySettings vocabulary)
    {
        vocabulary.CustomWords = new List<string>(CustomWords);
    }
}
