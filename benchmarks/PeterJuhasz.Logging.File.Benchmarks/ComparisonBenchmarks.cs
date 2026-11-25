using System.Globalization;
using BenchmarkDotNet.Attributes;
using NLog.Extensions.Logging;
using NLog.Targets;
using NLog.Targets.Wrappers;
using Serilog;

namespace PeterJuhasz.Logging.File.Benchmarks;

/// <summary>
/// Measures logging entries from multiple threads to a file, until all of them are flushed on disposal, compared to other libraries.
/// </summary>
/// <remarks>
/// The other libraries are configured to write asynchronously, without losing entries, like this library: Serilog blocks when its queue is full, NLog grows its queue.
/// </remarks>
[MemoryDiagnoser]
public class ComparisonBenchmarks
{
	private const int Entries = 100_000;
	private const string Category = "PeterJuhasz.Logging.File.Benchmarks.ComparisonBenchmarks";

	private readonly string _directory = Path.Combine(Path.GetTempPath(), "PeterJuhasz.Logging.File.Benchmarks", Guid.NewGuid().ToString("N"));

	[Params(1, 4)]
	public int Threads { get; set; }

	[GlobalSetup]
	public void Setup() => Directory.CreateDirectory(_directory);

	[IterationCleanup]
	public void DeleteFiles()
	{
		foreach (var file in Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories))
		{
			System.IO.File.Delete(file);
		}
	}

	[GlobalCleanup]
	public void Cleanup() => Directory.Delete(_directory, recursive: true);

	[Benchmark(Baseline = true, OperationsPerInvoke = Entries)]
	public void PeterJuhasz()
	{
		using var factory = LoggerFactory.Create(builder => builder.AddFile(Path.Combine(_directory, "PeterJuhasz")));
		Log(factory);
	}

	[Benchmark(OperationsPerInvoke = Entries)]
	public void Serilog()
	{
		var logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Async(sink => sink.File(Path.Combine(_directory, "Serilog", "log.txt"), formatProvider: CultureInfo.InvariantCulture, buffered: true), bufferSize: Entries, blockWhenFull: true)
			.CreateLogger();

		using var factory = LoggerFactory.Create(builder => builder.AddSerilog(logger, dispose: true));
		Log(factory);
	}

	[Benchmark(OperationsPerInvoke = Entries)]
	public void NLog()
	{
		var configuration = new global::NLog.Config.LoggingConfiguration();
		using var file = new FileTarget("file")
		{
			FileName = Path.Combine(_directory, "NLog", "log.txt"),
			Layout = "${longdate} [${level:uppercase=true}] ${logger}: ${message}${onexception:${newline}${exception:format=tostring}}",
			KeepFileOpen = true,
			AutoFlush = false,
		};
		using var async = new AsyncTargetWrapper(file) { OverflowAction = AsyncTargetWrapperOverflowAction.Grow };
		configuration.AddRuleForAllLevels(async);

		using var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddNLog(configuration, new NLogProviderOptions { ShutdownOnDispose = true }));
		Log(factory);
	}

	private void Log(ILoggerFactory factory)
	{
		var logger = factory.CreateLogger(Category);
		var perThread = Entries / Threads;
		Parallel.For(0, Threads, new ParallelOptions { MaxDegreeOfParallelism = Threads }, thread =>
		{
			for (var index = 0; index < perThread; index++)
			{
				Messages.Processed(logger, index, "Contoso", 12.5);
			}
		});
	}
}
