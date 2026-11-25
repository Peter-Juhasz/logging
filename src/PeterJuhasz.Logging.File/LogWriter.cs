using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Unicode;

namespace PeterJuhasz.Logging.File;

/// <summary>
/// Collects log entries from any number of threads without blocking them, and writes them to a stream in batches on a dedicated thread.
/// </summary>
/// <remarks>
/// <para>Logging threads only append the entries to a lock-free queue, they never wait for locks, events, or I/O.
/// The writer thread polls the queue, so logging threads do not have to signal it either.</para>
/// <para>When the process exits, or crashes because of an unhandled exception, the pending entries are written, and the writer turns synchronous:
/// the entries logged afterwards, for example by the shutdown of the host or by other handlers of the same events, are written before the call returns,
/// as the process may terminate any moment.</para>
/// </remarks>
internal sealed class LogWriter : IDisposable
{
	/// <summary>
	/// The time the writer thread waits for new entries when the queue is empty.
	/// </summary>
	internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

	private const int BufferSize = 64 * 1024;

	/// <summary>
	/// The maximum length of the parts with fixed length: the timestamp, the level, the event identifier and the separators.
	/// </summary>
	private const int MaxFixedLength = 64;

	private const int Running = 0;
	private const int Synchronous = 1;
	private const int Completed = 2;

	private readonly ConcurrentQueue<LogEntry> _queue = new();
	private readonly Stream _stream;
	private readonly Thread _thread;
	private readonly ManualResetEventSlim _stopping = new();
	private readonly Lock _lock = new();
	private readonly byte[] _buffer = GC.AllocateUninitializedArray<byte>(BufferSize);
	private int _length;
	private volatile int _state;

	public LogWriter(Stream stream)
	{
		_stream = stream;
		_thread = new Thread(Run)
		{
			IsBackground = true,
			Name = "PeterJuhasz.Logging.File",
		};
		_thread.Start();

		AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
	}

	/// <summary>
	/// Gets whether there are no entries waiting to be written.
	/// </summary>
	internal bool IsIdle => _queue.IsEmpty;

	/// <summary>
	/// Queues an entry to be written, without blocking while the process runs.
	/// </summary>
	public void Enqueue(in LogEntry entry)
	{
		if (_state == Completed)
		{
			return;
		}

		_queue.Enqueue(entry);

		// read the state again, as the writer may have turned synchronous meanwhile, and its last drain may have missed this entry
		if (_state == Synchronous)
		{
			DrainSynchronously();
		}
	}

	/// <summary>
	/// Writes the pending entries on the calling thread, unless the writer has been disposed meanwhile.
	/// </summary>
	private void DrainSynchronously()
	{
		lock (_lock)
		{
			if (_state == Synchronous)
			{
				Drain();
			}
		}
	}

	/// <summary>
	/// Stops the writer thread and writes the pending entries, then writes the entries logged afterwards synchronously.
	/// </summary>
	internal void SwitchToSynchronous()
	{
		lock (_lock)
		{
			if (_state != Running)
			{
				return;
			}

			StopWriterThread();
			_state = Synchronous;

			// make the new state visible before draining, so the threads which enqueue meanwhile either see it, or their entries are drained here
			Interlocked.MemoryBarrier();
			Drain();
		}
	}

	/// <summary>
	/// Stops the writer thread, writes all the pending entries, and flushes them to the disk.
	/// </summary>
	public void Dispose()
	{
		lock (_lock)
		{
			if (_state == Completed)
			{
				return;
			}

			AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
			AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;

			StopWriterThread();

			// write the entries which were queued while the writer thread was stopping
			_state = Completed;
			Drain();

			try
			{
				if (_stream is FileStream file)
				{
					file.Flush(flushToDisk: true);
				}
				else
				{
					_stream.Flush();
				}
			}
			catch (IOException)
			{
				// nothing can be done about a failing file system on shutdown
			}

			_stream.Dispose();
			_stopping.Dispose();
		}
	}

	private void OnProcessExit(object? sender, EventArgs e) => SwitchToSynchronous();

	private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) => SwitchToSynchronous();

	private void StopWriterThread()
	{
		_stopping.Set();
		if (Environment.CurrentManagedThreadId != _thread.ManagedThreadId)
		{
			_thread.Join();
		}
	}

	private void Run()
	{
		while (true)
		{
			// read the signal before draining, so the entries queued before stopping are written for sure
			var stopping = _stopping.IsSet;
			Drain();
			if (stopping)
			{
				return;
			}

			_ = _stopping.Wait(PollInterval);
		}
	}

	private void Drain()
	{
		while (_queue.TryDequeue(out var entry))
		{
			Write(entry);
		}

		FlushBuffer();
	}

	private void Write(in LogEntry entry)
	{
		// 2026-11-25T10:12:40.1234567Z [INF] Category[42]: Message
		Reserve(MaxFixedLength);
		var span = _buffer.AsSpan(_length);
		_ = entry.Timestamp.TryFormat(span, out var written, "O", CultureInfo.InvariantCulture);
		span = span[written..];
		" ["u8.CopyTo(span);
		GetLevel(entry.Level).CopyTo(span[2..]);
		"] "u8.CopyTo(span[5..]);
		_length += written + 7;

		Append(entry.Category);

		Reserve(MaxFixedLength);
		if (entry.EventId.Id != 0)
		{
			span = _buffer.AsSpan(_length);
			span[0] = (byte)'[';
			_ = entry.EventId.Id.TryFormat(span[1..], out written, provider: CultureInfo.InvariantCulture);
			span[written + 1] = (byte)']';
			_length += written + 2;
		}

		": "u8.CopyTo(_buffer.AsSpan(_length));
		_length += 2;

		Append(entry.Message);
		AppendNewLine();

		if (entry.Exception is { } exception)
		{
			Append(exception);
			AppendNewLine();
		}
	}

	private static ReadOnlySpan<byte> GetLevel(LogLevel level) => level switch
	{
		LogLevel.Trace => "TRC"u8,
		LogLevel.Debug => "DBG"u8,
		LogLevel.Information => "INF"u8,
		LogLevel.Warning => "WRN"u8,
		LogLevel.Error => "ERR"u8,
		LogLevel.Critical => "CRT"u8,
		_ => "???"u8,
	};

	private void AppendNewLine()
	{
		Reserve(1);
		_buffer[_length++] = (byte)'\n';
	}

	private void Append(ReadOnlySpan<byte> bytes)
	{
		while (true)
		{
			var free = _buffer.AsSpan(_length);
			if (bytes.Length <= free.Length)
			{
				bytes.CopyTo(free);
				_length += bytes.Length;
				return;
			}

			bytes[..free.Length].CopyTo(free);
			_length += free.Length;
			bytes = bytes[free.Length..];
			FlushBuffer();
		}
	}

	private void Append(ReadOnlySpan<char> chars)
	{
		while (true)
		{
			var status = Utf8.FromUtf16(chars, _buffer.AsSpan(_length), out var read, out var written);
			_length += written;
			if (status != OperationStatus.DestinationTooSmall)
			{
				return;
			}

			chars = chars[read..];
			FlushBuffer();
		}
	}

	private void Reserve(int length)
	{
		if (_buffer.Length - _length < length)
		{
			FlushBuffer();
		}
	}

	private void FlushBuffer()
	{
		if (_length == 0)
		{
			return;
		}

		try
		{
			_stream.Write(_buffer, 0, _length);
		}
		catch (IOException)
		{
			// the batch is lost if the file system fails, but the logger keeps working, and may succeed with the next batch
		}

		_length = 0;
	}
}