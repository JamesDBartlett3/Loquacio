namespace WhisperDictation.Services;

/// <summary>
/// Manages the history log for processed dictation segments.
/// </summary>
public interface IHistoryService
{
    /// <summary>
    /// Adds a new entry to the history log.
    /// </summary>
    /// <param name="entry">The history entry to add</param>
    Task AddEntryAsync(HistoryEntry entry);

    /// <summary>
    /// Gets all history entries, most recent first.
    /// </summary>
    /// <returns>List of history entries</returns>
    Task<List<HistoryEntry>> GetHistoryAsync();

    /// <summary>
    /// Gets a limited number of recent history entries.
    /// </summary>
    /// <param name="count">Number of entries to retrieve</param>
    /// <returns>List of recent history entries</returns>
    Task<List<HistoryEntry>> GetRecentHistoryAsync(int count);

    /// <summary>
    /// Clears all history entries.
    /// </summary>
    Task ClearHistoryAsync();

    /// <summary>
    /// Deletes a specific history entry by ID.
    /// </summary>
    /// <param name="id">The entry ID to delete</param>
    Task DeleteEntryAsync(Guid id);

    /// <summary>
    /// Gets the total number of history entries.
    /// </summary>
    Task<int> GetCountAsync();
}