using System.Runtime.CompilerServices;
using System.Text;
using ExtractorTester.Models;

[assembly: InternalsVisibleTo("ExtractorTester.Tests")]

namespace ExtractorTester.Output;

/// <summary>Run-fatal output problem carrying a tool-error code.</summary>
public sealed class OutputError(ErrorCode code, string message) : Exception(message)
{
    /// <summary>OUTPUT_EXISTS or OUTPUT_WRITE_FAILED.</summary>
    public ErrorCode Code { get; } = code;
}

/// <summary>
/// Port of src/extractor_tester/output.py: output folder rules, streaming JSONL writers,
/// atomic report.json. All files are UTF-8 without BOM with LF newlines.
/// </summary>
public static class ReportWriter
{
    public const string PairsFile = "pairs.jsonl";
    public const string ItemsFile = "items.jsonl";
    public const string ReportFile = "report.json";

    internal static readonly UTF8Encoding Utf8NoBom = new(false);

    internal static bool IsIoError(Exception e) => e is IOException or UnauthorizedAccessException;

    internal static OutputError WriteFailed(string path, Exception e) =>
        new(ErrorCode.OUTPUT_WRITE_FAILED, $"cannot write {path}: {e.Message}");

    /// <summary>Create the output folder; refuse a non-empty one unless overwrite is set.</summary>
    public static string PrepareOut(string outDir, bool overwrite)
    {
        try
        {
            if (File.Exists(outDir))
            {
                throw new OutputError(ErrorCode.OUTPUT_WRITE_FAILED, $"{outDir} is not a directory");
            }
            if (Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any() && !overwrite)
            {
                throw new OutputError(
                    ErrorCode.OUTPUT_EXISTS,
                    $"output folder {outDir} is not empty; use --overwrite to reuse it");
            }
            Directory.CreateDirectory(outDir);
        }
        catch (Exception e) when (IsIoError(e))
        {
            throw WriteFailed(outDir, e);
        }
        return outDir;
    }

    /// <summary>Open pairs.jsonl in the output folder.</summary>
    public static JsonlWriter PairsWriter(string outDir) => new(Path.Combine(outDir, PairsFile));

    /// <summary>Open items.jsonl in the output folder.</summary>
    public static JsonlWriter ItemsWriter(string outDir) => new(Path.Combine(outDir, ItemsFile));

    /// <summary>Write report.json atomically: temp file in the same folder, then rename.</summary>
    public static string WriteReport(string outDir, Report report) =>
        WriteReport(outDir, report, static (src, dst) => File.Move(src, dst, overwrite: true));

    /// <summary>As <see cref="WriteReport(string, Report)"/> with the rename step injectable (tests).</summary>
    internal static string WriteReport(string outDir, Report report, Action<string, string> replace)
    {
        var target = Path.Combine(outDir, ReportFile);
        string? tmp = null;
        try
        {
            tmp = CreateTemp(outDir);
            using (var fs = new FileStream(tmp, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                var bytes = Utf8NoBom.GetBytes(PyJson.Serialize(report, indent: true));
                fs.Write(bytes);
                fs.Flush(flushToDisk: true);
            }
            replace(tmp, target);
        }
        catch (Exception e)
        {
            if (tmp is not null)
            {
                try
                {
                    File.Delete(tmp);
                }
                catch (Exception de) when (IsIoError(de))
                {
                }
            }
            if (IsIoError(e))
            {
                throw WriteFailed(target, e);
            }
            throw;
        }
        return target;
    }

    // tempfile.mkstemp(dir=out, prefix=".report-", suffix=".tmp"): exclusive create.
    private static string CreateTemp(string outDir)
    {
        for (var attempt = 0; ; attempt++)
        {
            var path = Path.Combine(outDir, ".report-" + Path.GetRandomFileName().Replace(".", "") + ".tmp");
            try
            {
                using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                }
                return path;
            }
            catch (IOException) when (File.Exists(path) && attempt < 100)
            {
            }
        }
    }
}

/// <summary>Append one model per line as UTF-8 JSON; truncates any existing file on open.</summary>
public sealed class JsonlWriter : IDisposable
{
    private readonly StreamWriter _writer;
    private bool _closed;

    /// <summary>Open the file for writing.</summary>
    public JsonlWriter(string path)
    {
        Path = path;
        try
        {
            _writer = new StreamWriter(
                new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read),
                ReportWriter.Utf8NoBom)
            {
                NewLine = "\n",
            };
        }
        catch (Exception e) when (ReportWriter.IsIoError(e))
        {
            throw ReportWriter.WriteFailed(path, e);
        }
    }

    /// <summary>The file being written.</summary>
    public string Path { get; }

    /// <summary>Write one model as a single JSON line.</summary>
    public void Write(object model)
    {
        try
        {
            _writer.Write(PyJson.Serialize(model));
            _writer.Write('\n');
        }
        catch (Exception e) when (ReportWriter.IsIoError(e))
        {
            throw ReportWriter.WriteFailed(Path, e);
        }
    }

    /// <summary>Flush and close the file.</summary>
    public void Close()
    {
        if (_closed)
        {
            return;
        }
        _closed = true;
        try
        {
            _writer.Dispose();
        }
        catch (Exception e) when (ReportWriter.IsIoError(e))
        {
            throw ReportWriter.WriteFailed(Path, e);
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Close();
}
