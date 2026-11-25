namespace PeterJuhasz.Logging.File.Benchmarks;

/// <summary>
/// Log messages generated at compile time, as recommended for high performance logging.
/// </summary>
internal static partial class Messages
{
	[LoggerMessage(Level = LogLevel.Information, Message = "Application started")]
	public static partial void Started(ILogger logger);

	[LoggerMessage(EventId = 42, Level = LogLevel.Information, Message = "Processed order {OrderId} for {Customer} in {Elapsed} ms")]
	public static partial void Processed(ILogger logger, int orderId, string customer, double elapsed);

	[LoggerMessage(Level = LogLevel.Error, Message = "Failed to process order {OrderId}")]
	public static partial void Failed(ILogger logger, Exception exception, int orderId);
}
