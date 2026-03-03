using BodyImageMigrator.Models;

namespace BodyImageMigrator;

/// <summary>
/// BODYINFO の DRAWDATA/BGDATA を S3 に移行するオーケストレーション。
/// </summary>
public class MigrationService
{
    private readonly MigratorOptions _options;
    private readonly int _parallel;

    public MigrationService(MigratorOptions options)
    {
        _options = options;
        _parallel = Math.Clamp(_options.Parallel, 1, 10);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var config = MigrationConfig.Load();
        var repository = new BodyInfoRepository(config.DbConnectionString);

        var baseDir = _options.GetBaseDirectory();
        var logDir = _options.GetLogDirectory();

        // ② DB接続確認
        try
        {
            await repository.TestConnectionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"DB に接続できません（接続文字列・ネットワークを確認してください）: {ex.Message}", ex);
        }

        using var logger = new CsvLogger(logDir);
        using var uploader = !string.IsNullOrWhiteSpace(_options.AwsAccessKeyId) && !string.IsNullOrWhiteSpace(_options.AwsSecretAccessKey)
            ? new S3Uploader(_options.AwsAccessKeyId!, _options.AwsSecretAccessKey!, config.AwsRegion, config.AwsBucket, _options.Overwrite)
            : new S3Uploader(config.AwsRegion, config.AwsBucket, _options.Overwrite);

        // ③ AWSアクセス確認（DryRun の場合はスキップ）
        if (!_options.DryRun)
        {
            try
            {
                await uploader.TestConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"AWS S3 に接続できません（認証・ネットワーク・タイムアウトを確認してください）: {ex.Message}", ex);
            }
        }

        // DB・AWS 確認後に既存ログファイルを削除
        DeleteExistingLogFiles(baseDir, logDir);
        Directory.CreateDirectory(baseDir);

        var startTime = DateTime.Now;
        var timestamp = startTime.ToString("yyyyMMdd_HHmmss");
        var summaryPath = Path.Combine(baseDir, $"run_{timestamp}.log");

        Console.WriteLine($"ログ出力先: {baseDir}");
        if (_options.DryRun)
            Console.WriteLine("【DRY-RUN】アップロードは行いません。");

        // ④ キーをバッチ取得し、1件ずつ画像を取得して処理（メモリ安全）
        var (totalProcessed, keysSql) = await ProcessInBatchesAsync(repository, uploader, logger, cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"取得件数: {totalProcessed} 件（MEMONO 昇順）");

        var endTime = DateTime.Now;
        var duration = endTime - startTime;
        var totalTarget = logger.SuccessCount + logger.FailureCount + logger.SkippedCount;

        var executedSql = string.IsNullOrEmpty(keysSql)
            ? BodyInfoRepository.ImageDataSql
            : keysSql + Environment.NewLine + Environment.NewLine + "画像取得:" + Environment.NewLine + BodyInfoRepository.ImageDataSql;
        WriteSummaryLog(summaryPath, startTime, endTime, duration, totalTarget, logger, totalProcessed, executedSql);

        Console.WriteLine("処理完了。");
    }

    /// <summary>前回実行で出力したログファイルを削除する。baseDir の run_*.log と logDir 内の全ファイル。</summary>
    private static void DeleteExistingLogFiles(string baseDir, string logDir)
    {
        if (Directory.Exists(baseDir))
        {
            foreach (var path in Directory.GetFiles(baseDir, "run_*.log"))
            {
                try { File.Delete(path); } catch { /* 他プロセスで使用中などは無視 */ }
            }
        }
        if (Directory.Exists(logDir))
        {
            foreach (var path in Directory.GetFiles(logDir))
            {
                try { File.Delete(path); } catch { /* 他プロセスで使用中などは無視 */ }
            }
        }
    }

    private void WriteSummaryLog(
        string path,
        DateTime startTime,
        DateTime endTime,
        TimeSpan duration,
        int totalTarget,
        CsvLogger logger,
        int recordCount,
        string executedSql)
    {
        var lines = new List<string>
        {
            $"StartTime: {startTime:yyyy-MM-dd HH:mm:ss}",
            $"Parameters: {_options.ToParametersString()}",
            $"TotalTarget: {totalTarget}",
            $"RecordCount: {recordCount}",
            $"Success: {logger.SuccessCount}",
            $"Failure: {logger.FailureCount}",
            $"Skipped: {logger.SkippedCount}",
            $"EndTime: {endTime:yyyy-MM-dd HH:mm:ss}",
            $"Duration: {duration:hh\\:mm\\:ss}",
            $"ParallelCount: {_parallel}",
            "",
            "ExecutedSql:",
            executedSql
        };

        var content = string.Join(Environment.NewLine, lines);
        File.WriteAllText(path, content, System.Text.Encoding.UTF8);
    }

    /// <summary>キーをバッチ取得し、各キーで1件ずつ画像取得して処理。メモリ安全。</summary>
    private async Task<(int TotalProcessed, string KeysSql)> ProcessInBatchesAsync(
        BodyInfoRepository repository,
        S3Uploader uploader,
        CsvLogger logger,
        CancellationToken cancellationToken)
    {
        const int batchSize = 100;
        var lastMemono = 0L;
        var totalProcessed = 0;
        var remaining = _options.Limit;
        string keysSql = "";

        var semaphore = new SemaphoreSlim(_parallel);

        while (true)
        {
            var take = remaining.HasValue ? Math.Min(batchSize, remaining.Value - totalProcessed) : batchSize;
            if (remaining.HasValue && take <= 0) break;

            var (keys, sql) = await repository.GetRecordKeysBatchAsync(
                lastMemono,
                take,
                _options.MemonoFrom,
                _options.MemonoTo,
                _options.Istcd,
                _options.Ryono,
                _options.From,
                _options.To,
                _options.ExcludeDeleted,
                cancellationToken).ConfigureAwait(false);

            if (keys.Count == 0) break;
            if (string.IsNullOrEmpty(keysSql)) keysSql = sql;

            BodyInfoRepository.LogKeysSql(sql, lastMemono, take);
            BodyInfoRepository.LogImageDataSqlOnce();

            var tasks = keys.Select(key => ProcessKeyAsync(key, repository, uploader, logger, semaphore, cancellationToken));
            await Task.WhenAll(tasks).ConfigureAwait(false);

            totalProcessed += keys.Count;
            lastMemono = keys.Max(k => k.MEMONO);

            if (remaining.HasValue && totalProcessed >= remaining.Value) break;
        }

        return (totalProcessed, keysSql);
    }

    /// <summary>1キー分: 画像を取得して処理（1件ずつ取得でメモリ安全）</summary>
    private async Task ProcessKeyAsync(
        BodyInfoRepository.RecordKey key,
        BodyInfoRepository repository,
        S3Uploader uploader,
        CsvLogger logger,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (drawData, bgData) = await repository.GetImageDataAsync(key.MEMONO, cancellationToken).ConfigureAwait(false);

            var record = new BodyInfoRecord
            {
                MEMONO = key.MEMONO,
                ISTCD = key.ISTCD,
                RYONO = key.RYONO,
                DRAWDATA = drawData,
                BGDATA = bgData,
                DLTFLG = false
            };

            await ProcessRecordAsync(record, uploader, logger, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task ProcessRecordAsync(
        BodyInfoRecord record,
        S3Uploader uploader,
        CsvLogger logger,
        CancellationToken cancellationToken)
    {
        var istcd = record.ISTCD ?? "";
        var ryono = record.RYONO;
        var memono = record.MEMONO;

        var drawEmpty = string.IsNullOrWhiteSpace(record.DRAWDATA);
        var bgEmpty = string.IsNullOrWhiteSpace(record.BGDATA);

        if (drawEmpty && bgEmpty)
        {
            logger.LogSkipped(istcd, ryono, memono, "SKIPPED_EMPTY_DATA");
            return;
        }

        if (!drawEmpty)
            await ProcessOneAsync(record, istcd, ryono, memono, isBg: false, record.DRAWDATA!, uploader, logger, cancellationToken).ConfigureAwait(false);

        if (!bgEmpty)
            await ProcessOneAsync(record, istcd, ryono, memono, isBg: true, record.BGDATA!, uploader, logger, cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessOneAsync(
        BodyInfoRecord record,
        string istcd,
        int ryono,
        long memono,
        bool isBg,
        string data,
        S3Uploader uploader,
        CsvLogger logger,
        CancellationToken cancellationToken)
    {
        var s3Key = S3Uploader.GetS3Key(istcd, ryono, memono, isBg);

        var bytes = DataUrlHelper.TryDecodeToImageBytes(data);
        if (bytes == null || bytes.Length == 0)
        {
            var kind = isBg ? "BGDATA" : "DRAWDATA";
            logger.LogSkipped(istcd, ryono, memono, $"SKIPPED_DECODE_FAILED_{kind}");
            return;
        }

        if (_options.DryRun)
        {
            logger.LogSuccess(istcd, ryono, memono, s3Key, "DRY_RUN");
            return;
        }

        var (uploaded, error) = await uploader.PutImageAsync(s3Key, bytes, cancellationToken).ConfigureAwait(false);
        if (error != null)
            logger.LogFailure(istcd, ryono, memono, error);
        else if (uploaded)
            logger.LogSuccess(istcd, ryono, memono, s3Key, "SUCCESS");
        else
            logger.LogSuccess(istcd, ryono, memono, s3Key, "SKIPPED_ALREADY_EXISTS");
    }
}
