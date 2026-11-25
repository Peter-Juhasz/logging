namespace PeterJuhasz.Logging.File;

/// <summary>
/// Captures log entries of a category, and hands them over to the <see cref="LogWriter"/> without blocking.
/// </summary>
/// <remarks>
/// Scopes are not supported, as capturing them would slow down every call.
/// </remarks>
internal sealed class FileLogger(LogWriter writer, byte[] category) : ILogger
{
	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		if (logLevel == LogLevel.None)
		{
			return;
		}

		ArgumentNullException.ThrowIfNull(formatter);

		writer.Enqueue(new LogEntry(DateTime.UtcNow, logLevel, category, eventId, formatter(state, exception), exception?.ToString()));
	}
}
