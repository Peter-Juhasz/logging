using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PeterJuhasz.Logging.File;

namespace Microsoft.Extensions.Logging;

/// <summary>
/// Adds the file logger to the logging builder.
/// </summary>
public static class FileLoggingBuilderExtensions
{
	/// <summary>
	/// Adds a logger which writes to a single file per application instance in the <c>logs</c> directory next to the application.
	/// </summary>
	/// <param name="builder">The logging builder.</param>
	/// <returns>The logging builder.</returns>
	public static ILoggingBuilder AddFile(this ILoggingBuilder builder) => builder.AddFile(FileLoggerProvider.DefaultDirectory);

	/// <summary>
	/// Adds a logger which writes to a single file per application instance in a directory.
	/// </summary>
	/// <param name="builder">The logging builder.</param>
	/// <param name="directory">The directory to write the log file into, which is created if it does not exist.</param>
	/// <returns>The logging builder.</returns>
	public static ILoggingBuilder AddFile(this ILoggingBuilder builder, string directory)
	{
		ArgumentNullException.ThrowIfNull(builder);
		ArgumentException.ThrowIfNullOrEmpty(directory);

		return builder.AddProvider(_ => new FileLoggerProvider(directory));
	}

	/// <summary>
	/// Adds a logger which writes to a file with a fixed name in a directory, instead of one named after the start time and the identifier of the process.
	/// </summary>
	/// <param name="builder">The logging builder.</param>
	/// <param name="directory">The directory to write the log file into, which is created if it does not exist.</param>
	/// <param name="fileName">The name of the log file, which is appended to if it exists.</param>
	/// <returns>The logging builder.</returns>
	public static ILoggingBuilder AddFile(this ILoggingBuilder builder, string directory, string fileName)
	{
		ArgumentNullException.ThrowIfNull(builder);
		ArgumentException.ThrowIfNullOrEmpty(directory);
		ArgumentException.ThrowIfNullOrEmpty(fileName);

		return builder.AddProvider(_ => new FileLoggerProvider(directory, fileName));
	}

	private static ILoggingBuilder AddProvider(this ILoggingBuilder builder, Func<IServiceProvider, FileLoggerProvider> factory)
	{
		builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, FileLoggerProvider>(factory));
		return builder;
	}
}
