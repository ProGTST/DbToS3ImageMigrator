using System.Text.Json;

namespace BodyImageMigrator;

/// <summary>
/// publish\BodyImageMigration\Config\development.json から DB 接続・AWS リージョン・バケットを読み込む。
/// 必ず publish\BodyImageMigration\Config を参照する（exe の位置から親方向に "publish" フォルダを探す）。
/// </summary>
public static class MigrationConfig
{
    private const string ConfigFileName = "development.json";
    private const string PublishFolderName = "publish";
    private const string BodyImageMigrationFolderName = "BodyImageMigration";

    /// <summary>publish\BodyImageMigration のフルパス。UserAccessControl・Config・ログ出力の共通ベース。親方向に publish を探索。</summary>
    public static string GetBodyImageMigrationBaseDirectory()
    {
        var dir = Path.GetFullPath(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, '/'));
        while (!string.IsNullOrEmpty(dir))
        {
            var publishDir = Path.Combine(dir, PublishFolderName);
            if (Directory.Exists(publishDir))
                return Path.Combine(publishDir, BodyImageMigrationFolderName);
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException(
            "publish フォルダを参照できません。publish\\BodyImageMigration を配置してください。");
    }

    /// <summary>設定ファイルのパス。必ず …\publish\BodyImageMigration\Config\development.json</summary>
    public static string GetConfigPath()
    {
        return Path.Combine(GetBodyImageMigrationBaseDirectory(), "Config", ConfigFileName);
    }

    /// <summary>development.json を読み込む。ファイルがない・不正な場合は例外。</summary>
    public static MigrationConfigDto Load()
    {
        var path = GetConfigPath();
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"設定ファイルが見つかりません: {path}\r\nBodyImageMigration\\Config\\development.json を配置してください。");

        var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var root = JsonSerializer.Deserialize<MigrationConfigRoot>(json, options);
        if (root?.Db == null || root.Aws == null)
            throw new InvalidOperationException($"設定ファイルの形式が不正です: {path}（db / aws を指定してください）");

        var conn = root.Db.ConnectionString?.Trim();
        if (string.IsNullOrEmpty(conn))
            throw new InvalidOperationException($"設定ファイルに db.connectionString がありません: {path}");

        var region = root.Aws.Region?.Trim() ?? "ap-northeast-1";
        var bucket = root.Aws.Bucket?.Trim();
        if (string.IsNullOrEmpty(bucket))
            throw new InvalidOperationException($"設定ファイルに aws.bucket がありません: {path}");

        return new MigrationConfigDto(conn, region, bucket);
    }

    public sealed class MigrationConfigDto
    {
        public string DbConnectionString { get; }
        public string AwsRegion { get; }
        public string AwsBucket { get; }

        internal MigrationConfigDto(string dbConnectionString, string awsRegion, string awsBucket)
        {
            DbConnectionString = dbConnectionString;
            AwsRegion = awsRegion;
            AwsBucket = awsBucket;
        }
    }

    private sealed class MigrationConfigRoot
    {
        public DbSection? Db { get; set; }
        public AwsSection? Aws { get; set; }
    }

    private sealed class DbSection
    {
        public string? ConnectionString { get; set; }
    }

    private sealed class AwsSection
    {
        public string? Region { get; set; }
        public string? Bucket { get; set; }
    }
}
