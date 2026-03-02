namespace BodyImageMigrator;

/// <summary>
/// 成功・失敗・スキップのレコード単位CSVを BodyImageMigrationLog に出力する。
/// 並列実行時も安全に書き込むため、Writer のオープンと書き込みをロックする。
/// </summary>
public class CsvLogger : IDisposable
{
    private readonly string _logDirectory;
    private readonly string _timestamp;
    private readonly object _writeLock = new();
    private StreamWriter? _successWriter;
    private StreamWriter? _failureWriter;
    private StreamWriter? _skippedWriter;

    private static readonly string SuccessHeader = "ISTCD,RYONO,MEMONO,S3KEY,RESULT,TIMESTAMP";
    private static readonly string FailureHeader = "ISTCD,RYONO,MEMONO,ERROR_MESSAGE";
    private static readonly string SkippedHeader = "ISTCD,RYONO,MEMONO,REASON";

    public int SuccessCount { get; private set; }
    public int FailureCount { get; private set; }
    public int SkippedCount { get; private set; }

    public CsvLogger(string logDirectory)
    {
        _logDirectory = logDirectory;
        _timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        Directory.CreateDirectory(_logDirectory);
    }

    private StreamWriter OpenSuccessWriter()
    {
        lock (_writeLock)
        {
            if (_successWriter != null) return _successWriter;
            var path = Path.Combine(_logDirectory, $"success_{_timestamp}.csv");
            _successWriter = new StreamWriter(path, false, System.Text.Encoding.UTF8);
            _successWriter.WriteLine(SuccessHeader);
            return _successWriter;
        }
    }

    private StreamWriter OpenFailureWriter()
    {
        lock (_writeLock)
        {
            if (_failureWriter != null) return _failureWriter;
            var path = Path.Combine(_logDirectory, $"failure_{_timestamp}.csv");
            _failureWriter = new StreamWriter(path, false, System.Text.Encoding.UTF8);
            _failureWriter.WriteLine(FailureHeader);
            return _failureWriter;
        }
    }

    private StreamWriter OpenSkippedWriter()
    {
        lock (_writeLock)
        {
            if (_skippedWriter != null) return _skippedWriter;
            var path = Path.Combine(_logDirectory, $"skipped_{_timestamp}.csv");
            _skippedWriter = new StreamWriter(path, false, System.Text.Encoding.UTF8);
            _skippedWriter.WriteLine(SkippedHeader);
            return _skippedWriter;
        }
    }

    /// <summary>成功ログ: ISTCD,RYONO,MEMONO,S3KEY,RESULT,TIMESTAMP</summary>
    public void LogSuccess(string? istcd, int ryono, long memono, string s3Key, string result = "SUCCESS")
    {
        lock (_writeLock)
        {
            var w = OpenSuccessWriter();
            var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            w.WriteLine($"{CsvEscape(istcd)},{ryono},{memono},{CsvEscape(s3Key)},{CsvEscape(result)},{ts}");
            w.Flush();
            SuccessCount++;
        }
    }

    /// <summary>失敗ログ: ISTCD,RYONO,MEMONO,ERROR_MESSAGE</summary>
    public void LogFailure(string? istcd, int ryono, long memono, string errorMessage)
    {
        lock (_writeLock)
        {
            var w = OpenFailureWriter();
            w.WriteLine($"{CsvEscape(istcd)},{ryono},{memono},{CsvEscape(errorMessage)}");
            w.Flush();
            FailureCount++;
        }
    }

    /// <summary>スキップログ: ISTCD,RYONO,MEMONO,REASON（DRAWDATA NULL / BGDATA NULL 等）</summary>
    public void LogSkipped(string? istcd, int ryono, long memono, string reason)
    {
        lock (_writeLock)
        {
            var w = OpenSkippedWriter();
            w.WriteLine($"{CsvEscape(istcd)},{ryono},{memono},{CsvEscape(reason)}");
            w.Flush();
            SkippedCount++;
        }
    }

    private static string CsvEscape(string? value)
    {
        if (value == null) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    public string Timestamp => _timestamp;

    public void Dispose()
    {
        _successWriter?.Dispose();
        _failureWriter?.Dispose();
        _skippedWriter?.Dispose();
    }
}
