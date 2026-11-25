namespace PeterJuhasz.Logging.File.Tests;

/// <summary>
/// A unique temporary directory for a test, deleted with its contents on disposal.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
	public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PeterJuhasz.Logging.File.Tests", Guid.NewGuid().ToString("N"));

	public void Dispose()
	{
		if (Directory.Exists(Path))
		{
			Directory.Delete(Path, recursive: true);
		}
	}
}
