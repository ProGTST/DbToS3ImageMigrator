using System.Globalization;

namespace BodyImageMigrator;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (!TryParseOptions(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            PrintUsage();
            return 1;
        }

        if (!ResolveCredentials(options))
        {
            Console.Error.WriteLine("AWS 認証情報を取得できませんでした。");
            return 1;
        }

        try
        {
            var service = new MigrationService(options);
            await service.RunAsync(CancellationToken.None).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"エラー: {ex.Message}");
            return 1;
        }
    }

    private static bool TryParseOptions(string[] args, out MigratorOptions options, out string? error)
    {
        options = new MigratorOptions();
        error = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--dry-run":
                    options.DryRun = true;
                    break;
                case "--overwrite":
                    options.Overwrite = true;
                    break;
                case "--exclude-deleted":
                    options.ExcludeDeleted = true;
                    break;
                case "--limit":
                    if (i + 1 >= args.Length) { error = "--limit に値が必要です。"; return false; }
                    if (!int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit) || limit < 0)
                    { error = "--limit は 0 以上の整数を指定してください。"; return false; }
                    options.Limit = limit;
                    break;
                case "--istcd":
                    if (i + 1 >= args.Length) { error = "--istcd に値が必要です。"; return false; }
                    options.Istcd = args[++i];
                    break;
                case "--from":
                    if (i + 1 >= args.Length) { error = "--from に値が必要です。"; return false; }
                    options.From = ParseDateTime(args[++i]);
                    break;
                case "--to":
                    if (i + 1 >= args.Length) { error = "--to に値が必要です。"; return false; }
                    options.To = ParseDateTime(args[++i]);
                    break;
                case "--output":
                    if (i + 1 >= args.Length) { error = "--output に値が必要です。"; return false; }
                    options.OutputPath = args[++i];
                    break;
                case "--parallel":
                    if (i + 1 >= args.Length) { error = "--parallel に値が必要です。"; return false; }
                    if (!int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parallel) || parallel < 1 || parallel > 10)
                    { error = "--parallel は 1～10 を指定してください。"; return false; }
                    options.Parallel = parallel;
                    break;
                case "--help":
                case "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
                default:
                    error = $"不明なオプション: {arg}";
                    return false;
            }
        }

        return true;
    }

    /// <summary>ユーザー名を求め、TSV にあればそのキーで、なければアクセスキー・シークレットキーを入力させて options に設定する。</summary>
    private static bool ResolveCredentials(MigratorOptions options)
    {
        Console.Write("ユーザー名を入力してください: ");
        var username = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            Console.Error.WriteLine("ユーザー名が入力されていません。");
            return false;
        }

        var keys = UserAccessKeyStore.GetByUsername(username);
        if (keys.HasValue)
        {
            options.AwsAccessKeyId = keys.Value.AccessKeyId;
            options.AwsSecretAccessKey = keys.Value.SecretAccessKey;
            return true;
        }

        Console.Write("アクセスキーを入力してください: ");
        var accessKeyId = Console.ReadLine()?.Trim();
        Console.Write("シークレットアクセスキーを入力してください: ");
        var secretAccessKey = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrWhiteSpace(secretAccessKey))
        {
            Console.Error.WriteLine("アクセスキーとシークレットアクセスキーを両方入力してください。");
            return false;
        }

        UserAccessKeyStore.AddOrUpdate(username, accessKeyId, secretAccessKey);
        options.AwsAccessKeyId = accessKeyId;
        options.AwsSecretAccessKey = secretAccessKey;
        return true;
    }

    private static DateTime? ParseDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt;
        return null;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(@"
BodyImageMigrator - BODYINFO の身体図画像を DB から S3 に移行する

使い方:
  BodyImageMigrator [オプション]

オプション:
  --dry-run           アップロードせずに処理内容のみ実行
  --limit <n>         処理する最大件数（0以上、未指定は無制限）
  --overwrite         S3 に既に同一キーが存在する場合も上書き
  --istcd <コード>    施設コードで絞り込み
  --from <日付>       記録日時（RECDATE）の開始日（yyyy-MM-dd 等）
  --to <日付>         記録日時（RECDATE）の終了日
  --exclude-deleted   削除済み（DLTFLG=1）を対象から除外
  --output <パス>     ログCSVの出力先ベース（未指定時は カレント\BodyImageMigration）
  --parallel <n>      並列数（1～10、デフォルト 5）
  --help, -h          このヘルプを表示

実行時にユーザー名の入力が求められます。
  TSV に該当ユーザーがいる場合: そのキーで S3 にアクセスします。
  いない場合: アクセスキーとシークレットアクセスキーの入力が求められ、入力したキーで実行し TSV に登録します。

設定ファイル:
  BodyImageMigration\Config\development.json で DB 接続文字列・AWS リージョン・S3 バケットを指定します。
");
    }
}
