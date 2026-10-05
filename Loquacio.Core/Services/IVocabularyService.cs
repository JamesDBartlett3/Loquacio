namespace Loquacio.Services;

/// <summary>
/// Manages custom vocabulary words for LLM post-processing context
/// </summary>
public interface IVocabularyService
{
    /// <summary>
    /// Get all custom vocabulary words
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    Task<IReadOnlyList<string>> GetWordsAsync(CancellationToken ct = default);

    /// <summary>
    /// Add a word to the vocabulary
    /// </summary>
    Task<bool> AddWordAsync(string word, CancellationToken ct = default);

    /// <summary>
    /// Remove a word from the vocabulary
    /// </summary>
    Task<bool> RemoveWordAsync(string word, CancellationToken ct = default);

    /// <summary>
    /// Check if a word exists in the vocabulary
    /// </summary>
    Task<bool> ContainsWordAsync(string word, CancellationToken ct = default);

    /// <summary>
    /// Import a list of words (merges with existing)
    /// </summary>
    Task ImportWordsAsync(IEnumerable<string> words, CancellationToken ct = default);

    /// <summary>
    /// Clear all custom vocabulary words
    /// </summary>
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>
    /// Get the vocabulary as a formatted context string for LLM prompts
    /// </summary>
    Task<string> GetContextStringAsync(CancellationToken ct = default);

    /// <summary>
    /// Event fired when vocabulary changes
    /// </summary>
    event EventHandler? VocabularyChanged;
}
