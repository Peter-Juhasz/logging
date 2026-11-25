using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace PeterJuhasz.Logging.File.Tests;

[TestClass]
public sealed partial class FileLoggerTests
{
	public TestContext TestContext { get; set; } = null!;

	[GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z \[(?<level>[A-Z]{3})\] (?<rest>.*)$")]
	private static partial Regex LinePattern { get; }

	[TestMethod]
	public void NamesFileAfterStartTimeAndProcessId()
	{
		using var directory = new TempDirectory();
		using var provider = new FileLoggerProvider(directory.Path);
		using var process = Process.GetCurrentProcess();

		var expected = FileLoggerProvider.GetFileName(process.StartTime, Environment.ProcessId);
		Assert.AreEqual(Path.Combine(directory.Path, expected), provider.FilePath);
		Assert.IsTrue(System.IO.File.Exists(provider.FilePath));
		Assert.AreEqual("20261125T101240Z_42.log", FileLoggerProvider.GetFileName(new DateTime(2026, 11, 25, 10, 12, 40, DateTimeKind.Utc), 42));
	}

	[TestMethod]
	public void WritesEntries()
	{
		var lines = Log(logger =>
		{
			logger.Log(LogLevel.Information, new EventId(42, "Name"), "Hello", null, Format);
			logger.Log(LogLevel.Warning, default, "World", null, Format);
		}, "My.Category");

		Assert.HasCount(2, lines);
		AssertLine(lines[0], "INF", "My.Category[42]: Hello");
		AssertLine(lines[1], "WRN", "My.Category: World");
	}

	[TestMethod]
	public void WritesAllLevels()
	{
		var lines = Log(logger =>
		{
			foreach (var level in Enum.GetValues<LogLevel>())
			{
				logger.Log(level, default, "Message", null, Format);
			}
		});

		Assert.AreSequenceEqual(new[] { "TRC", "DBG", "INF", "WRN", "ERR", "CRT" }, lines.Select(line => LinePattern.Match(line).Groups["level"].Value).ToArray());
	}

	[TestMethod]
	public void WritesExceptions()
	{
		var exception = new InvalidOperationException("Boom");
		var lines = Log(logger => logger.Log(LogLevel.Error, default, "Failed", exception, Format));

		AssertLine(lines[0], "ERR", "Category: Failed");
		Assert.AreEqual(exception.ToString(), string.Join(Environment.NewLine, lines.Skip(1)));
	}

	[TestMethod]
	public void WritesMessagesLongerThanTheBuffer()
	{
		// multi-byte characters and surrogate pairs which cross the boundaries of the 64 KB buffer
		var message = string.Concat(Enumerable.Repeat("árvíztűrő tükörfúrógép 😀 ", 10_000));
		var category = new string('c', 100_000);

		var lines = Log(logger => logger.Log(LogLevel.Information, default, message, null, Format), category);

		AssertLine(lines.Single(), "INF", $"{category}: {message}");
	}

	[TestMethod]
	public void WritesAllPendingEntriesOnDispose()
	{
		const int Threads = 8;
		const int EntriesPerThread = 25_000;

		var lines = Log(logger => Parallel.For(0, Threads, thread =>
		{
			for (var index = 0; index < EntriesPerThread; index++)
			{
				logger.Log(LogLevel.Information, default, (thread, index), null, static (state, _) => string.Create(CultureInfo.InvariantCulture, $"{state.thread}-{state.index}"));
			}
		}));

		Assert.HasCount(Threads * EntriesPerThread, lines);
		Assert.HasCount(Threads * EntriesPerThread, lines.Distinct());
	}

	[TestMethod]
	public void AppendsToTheFileOfTheSameInstance()
	{
		using var directory = new TempDirectory();
		string path;
		using (var provider = new FileLoggerProvider(directory.Path))
		{
			provider.CreateLogger("Category").Log(LogLevel.Information, default, "First", null, Format);
			path = provider.FilePath;
		}

		using (var provider = new FileLoggerProvider(directory.Path))
		{
			provider.CreateLogger("Category").Log(LogLevel.Information, default, "Second", null, Format);
			Assert.AreEqual(path, provider.FilePath);
		}

		var lines = System.IO.File.ReadAllLines(path);
		Assert.HasCount(2, lines);
		AssertLine(lines[1], "INF", "Category: Second");
	}

	[TestMethod]
	public void IgnoresEntriesAfterDispose()
	{
		using var directory = new TempDirectory();
		var provider = new FileLoggerProvider(directory.Path);
		var logger = provider.CreateLogger("Category");
		provider.Dispose();
		provider.Dispose();

		logger.Log(LogLevel.Information, default, "Ignored", null, Format);

		Assert.IsEmpty(System.IO.File.ReadAllLines(provider.FilePath));
	}

	[TestMethod]
	public void IsEnabledForAllLevelsButNone()
	{
		using var directory = new TempDirectory();
		using var provider = new FileLoggerProvider(directory.Path);
		var logger = provider.CreateLogger("Category");

		Assert.IsTrue(logger.IsEnabled(LogLevel.Trace));
		Assert.IsTrue(logger.IsEnabled(LogLevel.Critical));
		Assert.IsFalse(logger.IsEnabled(LogLevel.None));
		Assert.IsNull(logger.BeginScope("Scope"));
	}

	[TestMethod]
	public void ReusesLoggersOfTheSameCategory()
	{
		using var directory = new TempDirectory();
		using var provider = new FileLoggerProvider(directory.Path);

		Assert.AreSame(provider.CreateLogger("Category"), provider.CreateLogger("Category"));
		Assert.AreNotSame(provider.CreateLogger("Category"), provider.CreateLogger("category"));
	}

	[TestMethod]
	public async Task WritesEntriesWhileRunning()
	{
		using var directory = new TempDirectory();
		using var provider = new FileLoggerProvider(directory.Path);

		provider.CreateLogger("Category").Log(LogLevel.Information, default, "Hello", null, Format);
		await Task.Delay(LogWriter.PollInterval * 5, TestContext.CancellationToken).ConfigureAwait(false);

		using var stream = new FileStream(provider.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using var reader = new StreamReader(stream);
		AssertLine((await reader.ReadLineAsync(TestContext.CancellationToken).ConfigureAwait(false))!, "INF", "Category: Hello");
	}

	[TestMethod]
	public void WritesSynchronouslyWhenTheProcessTerminates()
	{
		using var directory = new TempDirectory();
		using var provider = new FileLoggerProvider(directory.Path);
		var logger = provider.CreateLogger("Category");
		logger.Log(LogLevel.Information, default, "Pending", null, Format);

		provider.Writer.SwitchToSynchronous();
		Assert.HasCount(1, ReadShared(provider.FilePath));

		logger.Log(LogLevel.Critical, default, "Crashed", null, Format);
		var lines = ReadShared(provider.FilePath);
		Assert.HasCount(2, lines);
		AssertLine(lines[1], "CRT", "Category: Crashed");
	}

	[TestMethod]
	public void WritesToFileWithFixedName()
	{
		using var directory = new TempDirectory();
		var expected = Path.Combine(directory.Path, "nested", "app.log");
		using (var services = new ServiceCollection().AddLogging(builder => builder.AddFile(Path.Combine(directory.Path, "nested"), "app.log")).BuildServiceProvider())
		{
			Assert.AreEqual(expected, services.GetServices<ILoggerProvider>().OfType<FileLoggerProvider>().Single().FilePath);
			services.GetRequiredService<ILoggerFactory>().CreateLogger("Category").Log(LogLevel.Information, default, "Hello", null, Format);
		}

		AssertLine(System.IO.File.ReadAllLines(expected).Single(), "INF", "Category: Hello");
	}

	[TestMethod]
	public void RegistersWithDependencyInjection()
	{
		using var directory = new TempDirectory();
		string path;
		using (var services = new ServiceCollection().AddLogging(builder => builder.AddFile(directory.Path).AddFile(directory.Path)).BuildServiceProvider())
		{
			var provider = services.GetServices<ILoggerProvider>().OfType<FileLoggerProvider>().Single();
			path = provider.FilePath;
			services.GetRequiredService<ILogger<FileLoggerTests>>().Log(LogLevel.Information, new EventId(1), "Hello", null, Format);
		}

		AssertLine(System.IO.File.ReadAllLines(path).Single(), "INF", $"{typeof(FileLoggerTests).FullName}[1]: Hello");
	}

	private static string Format(string state, Exception? exception) => state;

	private static string[] Log(Action<ILogger> log, string category = "Category")
	{
		using var directory = new TempDirectory();
		string path;
		using (var provider = new FileLoggerProvider(directory.Path))
		{
			log(provider.CreateLogger(category));
			path = provider.FilePath;
		}

		return System.IO.File.ReadAllLines(path);
	}

	private static List<string> ReadShared(string path)
	{
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using var reader = new StreamReader(stream);
		var lines = new List<string>();
		while (reader.ReadLine() is { } line)
		{
			lines.Add(line);
		}

		return lines;
	}

	private static void AssertLine(string line, string level, string rest)
	{
		var match = LinePattern.Match(line);
		Assert.IsTrue(match.Success, line);
		Assert.AreEqual(level, match.Groups["level"].Value);
		Assert.AreEqual(rest, match.Groups["rest"].Value);
	}
}
