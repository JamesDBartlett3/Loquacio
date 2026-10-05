using System.Collections.ObjectModel;

namespace Loquacio.Avalonia.ViewModels;

/// <summary>
/// Vocabulary settings tab for the controller.
/// Custom words are sent to the daemon for LLM prompt context.
/// </summary>
public partial class VocabularySettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _newWord = string.Empty;

    public ObservableCollection<string> CustomWords { get; } = [];

    [RelayCommand]
    private void AddWord()
    {
        var word = NewWord.Trim();
        if (!string.IsNullOrEmpty(word) && !CustomWords.Contains(word))
        {
            CustomWords.Add(word);
            NewWord = string.Empty;
        }
    }

    [RelayCommand]
    private void RemoveWord(string word)
    {
        CustomWords.Remove(word);
    }
}
