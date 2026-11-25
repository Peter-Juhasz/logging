using BenchmarkDotNet.Attributes;

namespace PeterJuhasz.Logging.File.Benchmarks;

/// <summary>
/// Measures the cost of a call to the logger on the logging thread, which is what the application pays for each entry.
/// </summary>
/// <remarks>
/// The entries are written to <see cref="Stream.Null"/> to keep the disk out of the measurements, but they still go through the writer thread.
/// The invocation count is fixed, so the queue is drained after each iteration, which takes long enough to be measured precisely.
/// </remarks>
[MemoryDiagnoser]
[InvocationCount(1_000_000)]
public class LogBenchmarks : IDisposable
{
	private static readonly Exception Exception = CreateException();

	private FileLoggerProvider _provider = null!;
	private ILogger _logger = null!;

	[GlobalSetup]
	public void Setup()
	{
		_provider = new FileLoggerProvider(Stream.Null);
		_logger = _provider.CreateLogger("PeterJuhasz.Logging.File.Benchmarks.LogBenchmarks");
	}

	[IterationCleanup]
	public void WaitForWriter()
	{
		// keep the queue from growing across iterations, if the writer thread falls behind
		var wait = new SpinWait();
		while (!_provider.Writer.IsIdle)
		{
			wait.SpinOnce();
		}
	}

	[GlobalCleanup]
	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	[Benchmark(Baseline = true)]
	public void Constant() => Messages.Started(_logger);

	[Benchmark]
	public void Arguments() => Messages.Processed(_logger, 12345, "Contoso", 12.5);

	[Benchmark]
	public void WithException() => Messages.Failed(_logger, Exception, 12345);

	protected virtual void Dispose(bool disposing)
	{
		if (disposing)
		{
			_provider?.Dispose();
		}
	}

	private static InvalidOperationException CreateException()
	{
		try
		{
			throw new InvalidOperationException("The order is not valid.");
		}
		catch (InvalidOperationException exception)
		{
			return exception;
		}
	}
}
