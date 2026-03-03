namespace BodyImageMigrator;

/// <summary>
/// コマンドラインオプション
/// </summary>
public class MigratorOptions
{
    /// <summary>実際にはアップロードせず処理内容のみ実行（DB読み取り・ログ出力のみ）</summary>
    public bool DryRun { get; set; }

    /// <summary>処理する最大件数（未指定時は全件）。MEMONOでソートした先頭から</summary>
    public int? Limit { get; set; }

    /// <summary>S3に既に同一キーが存在する場合も上書きする</summary>
    public bool Overwrite { get; set; }

    /// <summary>施設コードで絞り込み（未指定時は全施設）</summary>
    public string? Istcd { get; set; }

    /// <summary>利用者Noで絞り込み（未指定時は全利用者）</summary>
    public int? Ryono { get; set; }

    /// <summary>記録日時（RECDATE）の開始日（含む）。未指定時は制限なし</summary>
    public DateTime? From { get; set; }

    /// <summary>記録日時（RECDATE）の終了日（含む）。未指定時は制限なし</summary>
    public DateTime? To { get; set; }

    /// <summary>削除済み（DLTFLG=1）を対象から除外する</summary>
    public bool ExcludeDeleted { get; set; }

    /// <summary>ログCSVの出力先のベースディレクトリ。未指定時は publish\BodyImageMigration</summary>
    public string? OutputPath { get; set; }

    /// <summary>並列数（1～10）。デフォルト5</summary>
    public int Parallel { get; set; } = 5;

    /// <summary>AWS アクセスキー（指定時は環境変数ではなくこちらを使用）</summary>
    public string? AwsAccessKeyId { get; set; }

    /// <summary>AWS シークレットアクセスキー（指定時は環境変数ではなくこちらを使用）</summary>
    public string? AwsSecretAccessKey { get; set; }

    /// <summary>ベースディレクトリ。OutputPath 未指定時は publish\BodyImageMigration</summary>
    public string GetBaseDirectory()
    {
        return string.IsNullOrWhiteSpace(OutputPath)
            ? MigrationConfig.GetBodyImageMigrationBaseDirectory()
            : Path.GetFullPath(OutputPath);
    }

    /// <summary>ログCSVは ベース\BodyImageMigrationLog\ に出力する</summary>
    public string GetLogDirectory()
    {
        return Path.Combine(GetBaseDirectory(), "BodyImageMigrationLog");
    }

    /// <summary>実行パラメータをログ用文字列に変換（パスワード・シークレットは含めない）</summary>
    public string ToParametersString()
    {
        var list = new List<string>();
        if (DryRun) list.Add("--dry-run");
        if (Limit.HasValue) list.Add($"--limit={Limit.Value}");
        if (Overwrite) list.Add("--overwrite");
        if (!string.IsNullOrWhiteSpace(Istcd)) list.Add($"--istcd={Istcd}");
        if (Ryono.HasValue) list.Add($"--ryono={Ryono.Value}");
        if (From.HasValue) list.Add($"--from={From.Value:yyyy-MM-dd HH:mm:ss}");
        if (To.HasValue) list.Add($"--to={To.Value:yyyy-MM-dd HH:mm:ss}");
        if (ExcludeDeleted) list.Add("--exclude-deleted");
        if (!string.IsNullOrWhiteSpace(OutputPath)) list.Add($"--output={OutputPath}");
        list.Add($"--parallel={Parallel}");
        return string.Join(" ", list);
    }
}
