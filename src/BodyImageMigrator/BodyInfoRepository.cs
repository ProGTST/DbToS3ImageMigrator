using System.Data;
using BodyImageMigrator.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace BodyImageMigrator;

/// <summary>
/// BODYINFO テーブルから移行対象レコードを取得する。
/// 接続文字列は MigrationConfig (development.json) から取得する。
/// </summary>
public class BodyInfoRepository
{
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
    /// 条件に合う BODYINFO を MEMONO 昇順で取得する。
    /// </summary>
    /// <returns>レコード一覧と実行SQL</returns>
    public async Task<(IReadOnlyList<BodyInfoRecord> Records, string Sql)> GetRecordsAsync(
        string? istcd,
        DateTime? from,
        DateTime? to,
        bool excludeDeleted,
        int? limit,
        CancellationToken cancellationToken = default)
    {
        var conditions = new List<string> { "1=1" };
        var param = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(istcd))
        {
            conditions.Add("ISTCD = @Istcd");
            param.Add("Istcd", istcd.Trim());
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
        // limit 未指定＝無制限。0以上指定時は TOP で制限（0 の場合は 0 件）
        var topClause = limit.HasValue ? "TOP (@Limit) " : "";

        var sql = $@"
SELECT {topClause}MEMONO, ISTCD, RYONO, DRAWDATA, BGDATA, ISNULL(CAST(DLTFLG AS TINYINT), 0) AS DLTFLG
FROM BODYINFO WITH (NOLOCK)
WHERE {whereClause}
ORDER BY MEMONO".Trim();

        if (limit.HasValue)
            param.Add("Limit", limit.Value);

        LogSql(sql, param);

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var list = (await conn.QueryAsync<BodyInfoRecord>(new CommandDefinition(sql, param, cancellationToken: cancellationToken))
            .ConfigureAwait(false)).ToList();

        return (list, sql);
    }

    /// <summary>実行SQLをコンソールに出力する（日付は yyyy-M-d H:mm:ss 形式）</summary>
    private static void LogSql(string sql, DynamicParameters param)
    {
        var paramStr = string.Join(", ", param.ParameterNames.Select(n =>
        {
            var v = param.Get<object>(n);
            var s = v is DateTime dt ? dt.ToString("yyyy-M-d H:mm:ss") : v?.ToString() ?? "";
            return $"{n}={s}";
        }));
        Console.WriteLine($"実行SQL: {sql}");
        if (!string.IsNullOrEmpty(paramStr))
            Console.WriteLine($"  パラメータ: {paramStr}");
    }
}
