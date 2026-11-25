namespace PeterJuhasz.Logging.File;

/// <summary>
/// A log entry captured on the thread which logged it, to be encoded and written later on the writer thread.
/// </summary>
/// <param name="Timestamp">The time of logging in UTC.</param>
/// <param name="Level">The level of the entry.</param>
/// <param name="Category">The name of the category, already encoded to UTF-8.</param>
/// <param name="EventId">The identifier of the event.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Exception">The formatted exception, if any.</param>
internal readonly record struct LogEntry(DateTime Timestamp, LogLevel Level, byte[] Category, EventId EventId, string Message, string? Exception);
