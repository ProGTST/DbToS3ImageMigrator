using System.Data;
using BodyImageMigrator.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace BodyImageMigrator;

/// <summary>
/// BODYINFO テーブルから移行対象レコードを取得する。
/// メモリ対策: ①キーのみバッチ取得 ②1件ずつ DRAWDATA/BGDATA 取得。
/// </summary>
public class BodyInfoRepository
{
    private const int DefaultBatchSize = 100;
    private readonly string _connectionString;

    /// <summary>接続文字列を指定してリポジトリを生成。通常は MigrationConfig.Load() の DbConnectionString を渡す。</summary>
    public BodyInfoRepository(string connectionString)
    {
        var conn = connectionString?.Trim();
        if (string.IsNullOrEmpty(conn))
            throw new ArgumentException("接続文字列を指定してください。", nameof(connectionString));
        _connectionString = conn;
    }

    /// <summary>DB に接続できるか確認する。接続失敗時は例外を投げる。</summary>
    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// ① キーのみバッチ取得。MEMONO > LastMemono でページング。buffered: false でストリーミング。
    /// </summary>
    /// <returns>キー一覧と実行SQL（ログ用）</returns>
    public async Task<(IReadOnlyList<RecordKey> Keys, string KeysSql)> GetRecordKeysBatchAsync(
        long lastMemono,
        int batchSize,
        long? memonoFrom,
        long? memonoTo,
        string? istcd,
        int? ryono,
        DateTime? from,
        DateTime? to,
        bool excludeDeleted,
        CancellationToken cancellationToken = default)
    {
        var conditions = new List<string> { "MEMONO > @LastMemono" };
        var param = new DynamicParameters();
        param.Add("LastMemono", lastMemono);
        param.Add("BatchSize", batchSize);

        if (memonoFrom.HasValue)
        {
            conditions.Add("MEMONO >= @MemonoFrom");
            param.Add("MemonoFrom", memonoFrom.Value);
        }

        if (memonoTo.HasValue)
        {
            conditions.Add("MEMONO <= @MemonoTo");
            param.Add("MemonoTo", memonoTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(istcd))
        {
            conditions.Add("ISTCD = @Istcd");
            param.Add("Istcd", istcd.Trim());
        }

        if (ryono.HasValue)
        {
            conditions.Add("RYONO = @Ryono");
            param.Add("Ryono", ryono.Value);
        }

        if (from.HasValue)
        {
            conditions.Add("RECDATE >= @From");
            param.Add("From", from.Value.Date);
        }

        if (to.HasValue)
        {
            conditions.Add("RECDATE <= @ToExclusive");
            param.Add("ToExclusive", new DateTime(to.Value.Year, to.Value.Month, to.Value.Day, 23, 59, 59));
        }

        if (excludeDeleted)
        {
            conditions.Add("(DLTFLG = 0 OR DLTFLG IS NULL)");
        }

        var whereClause = string.Join(" AND ", conditions);
        var sql = $@"
SELECT TOP (@BatchSize) MEMONO, ISTCD, RYONO
FROM BODYINFO WITH (NOLOCK)
WHERE {whereClause}
ORDER BY MEMONO".Trim();

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var cmd = new CommandDefinition(sql, param, transaction: null, commandTimeout: null, commandType: null, CommandFlags.None, cancellationToken);
        var list = (await conn.QueryAsync<RecordKey>(cmd).ConfigureAwait(false)).ToList();

        return (list, sql);
    }

    /// <summary>② 1件ずつ DRAWDATA, BGDATA を取得するSQL（ログ出力用）</summary>
    public const string ImageDataSql = "SELECT DRAWDATA, BGDATA FROM BODYINFO WITH (NOLOCK) WHERE MEMONO = @Memono";

    /// <summary>
    /// ② 1件ずつ DRAWDATA, BGDATA を取得（メモリ安全）
    /// </summary>
    public async Task<(string? DrawData, string? BgData)> GetImageDataAsync(long memono, CancellationToken cancellationToken = default)
    {
        var sql = ImageDataSql;
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var row = await conn.QuerySingleOrDefaultAsync<ImageDataRow>(
            new CommandDefinition(sql, new { Memono = memono }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row == null ? (null, null) : (row.DRAWDATA, row.BGDATA);
    }

    /// <summary>キー取得用SQLをログ出力</summary>
    public static void LogKeysSql(string sql, long lastMemono, int batchSize)
    {
        Console.WriteLine($"実行SQL(キー取得): {sql}");
        Console.WriteLine($"  パラメータ: LastMemono={lastMemono}, BatchSize={batchSize}");
    }

    private static bool _imageDataSqlLogged;

    /// <summary>画像取得用SQLをログ出力（1回のみ）</summary>
    public static void LogImageDataSqlOnce()
    {
        if (_imageDataSqlLogged) return;
        _imageDataSqlLogged = true;
        Console.WriteLine($"実行SQL(画像取得): {ImageDataSql}");
        Console.WriteLine($"  パラメータ: Memono=各レコードのMEMONO");
    }

    /// <summary>キー（MEMONO, ISTCD, RYONO）のみ。画像は別クエリで取得。</summary>
    public sealed class RecordKey
    {
        public long MEMONO { get; set; }
        public string? ISTCD { get; set; }
        public int RYONO { get; set; }
    }

    private sealed class ImageDataRow
    {
        public string? DRAWDATA { get; set; }
        public string? BGDATA { get; set; }
    }
}
