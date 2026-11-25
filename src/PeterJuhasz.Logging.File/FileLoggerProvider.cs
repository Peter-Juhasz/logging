using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PeterJuhasz.Logging.File;

/// <summary>
/// Writes the logs of the application instance into a single file, named after the start time and the identifier of the process.
/// </summary>
/// <remarks>
/// <para>Logging never blocks: entries are queued without locks, and written in batches on a dedicated thread.</para>
/// <para>All the pending entries are written when the provider is disposed, when the process exits, or when it crashes because of an unhandled exception.
/// Once the process is terminating, the entries are written synchronously, so the logs of the shutdown are not lost either.</para>
/// </remarks>
[ProviderAlias("File")]
public sealed class FileLoggerProvider : ILoggerProvider
{
	private readonly LogWriter _writer;
	private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);

	/// <summary>
	/// Creates a provider which writes to the <c>logs</c> directory next to the application.
	/// </summary>
	public FileLoggerProvider()
		: this(DefaultDirectory)
	{
	}

	/// <summary>
	/// Creates a provider which writes to a directory.
	/// </summary>
	/// <param name="directory">The directory to write the log file into, which is created if it does not exist.</param>
	public FileLoggerProvider(string directory)
		: this(directory, GetFileName(GetStartTime(), Environment.ProcessId))
	{
	}

	/// <summary>
	/// Creates a provider which writes to a file with a fixed name, instead of one named after the start time and the identifier of the process.
	/// </summary>
	/// <param name="directory">The directory to write the log file into, which is created if it does not exist.</param>
	/// <param name="fileName">The name of the log file, which is appended to if it exists.</param>
	/// <remarks>
	/// The file is locked for writing, so another instance of the application, which uses the same file, fails to create its provider.
	/// </remarks>
	public FileLoggerProvider(string directory, string fileName)
	{
		ArgumentException.ThrowIfNullOrEmpty(directory);
		ArgumentException.ThrowIfNullOrEmpty(fileName);

		FilePath = Path.GetFullPath(Path.Combine(directory, fileName));
		_ = Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
		_writer = new LogWriter(new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 0));
	}

	/// <summary>
	/// Creates a provider which writes to a stream, instead of a file.
	/// </summary>
	internal FileLoggerProvider(Stream stream)
	{
		FilePath = string.Empty;
		_writer = new LogWriter(stream);
	}

	/// <summary>
	/// The directory the parameterless constructor writes to: <c>logs</c> next to the application.
	/// </summary>
	internal static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "logs");

	/// <summary>
	/// Gets the full path of the log file.
	/// </summary>
	public string FilePath { get; }

	internal LogWriter Writer => _writer;

	/// <inheritdoc />
	public ILogger CreateLogger(string categoryName)
	{
		ArgumentNullException.ThrowIfNull(categoryName);

		return _loggers.GetOrAdd(categoryName, static (name, writer) => new FileLogger(writer, Encoding.UTF8.GetBytes(name)), _writer);
	}

	/// <summary>
	/// Writes all the pending entries to the file, and closes it.
	/// </summary>
	public void Dispose() => _writer.Dispose();

	/// <summary>
	/// Gets the name of the log file, like <c>20261125T101240Z_12345.log</c>.
	/// </summary>
	/// <param name="startTime">The start time of the process.</param>
	/// <param name="processId">The identifier of the process.</param>
	internal static string GetFileName(DateTime startTime, int processId) => string.Create(CultureInfo.InvariantCulture, $"{startTime.ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}_{processId}.log");

	private static DateTime GetStartTime()
	{
		try
		{
			using var process = Process.GetCurrentProcess();
			return process.StartTime;
		}
		catch (PlatformNotSupportedException)
		{
			return DateTime.UtcNow;
		}
	}
}
