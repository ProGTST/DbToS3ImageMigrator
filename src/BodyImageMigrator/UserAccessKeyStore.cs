using System.IO;
using System.Text;
using System.Text.Json;

namespace BodyImageMigrator;

/// <summary>
/// userAccessKey.json の読み書き。ユーザーごとに AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY を管理する。
/// コンソール・Runner の両方で利用可能。
/// </summary>
public static class UserAccessKeyStore
{
    private const string FileName = "userAccessKey.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>JSON を保存するディレクトリ: publish\BodyImageMigration\UserAccessControl</summary>
    private static string GetFilePath()
    {
        var dir = Path.Combine(MigrationConfig.GetBodyImageMigrationBaseDirectory(), "UserAccessControl");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, FileName);
    }

    private static List<UserAccessKeyEntry> LoadAll()
    {
        var path = GetFilePath();
        var dir = Path.GetDirectoryName(path);
        var tsvPath = dir != null ? Path.Combine(dir, "USER_ACCESS_KEY.tsv") : "";

        if (!File.Exists(path))
        {
            if (!string.IsNullOrEmpty(tsvPath) && File.Exists(tsvPath))
                MigrateFromTsv(tsvPath, path);
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                var list = JsonSerializer.Deserialize<List<UserAccessKeyEntry>>(json, JsonOptions);
                return list ?? new List<UserAccessKeyEntry>();
            }
            return new List<UserAccessKeyEntry>();
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var list = JsonSerializer.Deserialize<List<UserAccessKeyEntry>>(json, JsonOptions);
            return list ?? new List<UserAccessKeyEntry>();
        }
        catch
        {
            return new List<UserAccessKeyEntry>();
        }
    }

    /// <summary>旧 USER_ACCESS_KEY.tsv から userAccessKey.json へ移行（初回のみ）</summary>
    private static void MigrateFromTsv(string tsvPath, string jsonPath)
    {
        var list = new List<UserAccessKeyEntry>();
        foreach (var line in File.ReadLines(tsvPath, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("USERNAME\t", StringComparison.OrdinalIgnoreCase))
                continue;
            var cols = line.Split('\t');
            if (cols.Length >= 3 && !string.IsNullOrWhiteSpace(cols[0]))
                list.Add(new UserAccessKeyEntry
                {
                    Username = cols[0].Trim(),
                    AwsAccessKeyId = cols[1].Trim(),
                    AwsSecretAccessKey = cols[2].Trim()
                });
        }
        if (list.Count > 0)
        {
            var json = JsonSerializer.Serialize(list, JsonOptions);
            File.WriteAllText(jsonPath, json, new UTF8Encoding(false));
        }
    }

    private static void SaveAll(List<UserAccessKeyEntry> entries)
    {
        var path = GetFilePath();
        var json = JsonSerializer.Serialize(entries, JsonOptions);
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    /// <summary>指定ユーザーのキーを取得する。存在しなければ null。</summary>
    public static (string AccessKeyId, string SecretAccessKey)? GetByUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        var key = username.Trim();
        foreach (var e in LoadAll())
        {
            if (string.Equals(e.Username?.Trim(), key, StringComparison.OrdinalIgnoreCase))
                return (e.AwsAccessKeyId ?? "", e.AwsSecretAccessKey ?? "");
        }
        return null;
    }

    /// <summary>ユーザーのキーが登録されているか。</summary>
    public static bool Exists(string username) => GetByUsername(username) != null;

    /// <summary>JSON に追加または既存ユーザーを更新する。</summary>
    public static void AddOrUpdate(string username, string accessKeyId, string secretAccessKey)
    {
        if (string.IsNullOrWhiteSpace(username)) return;
        var key = username.Trim();
        var list = LoadAll();
        var found = list.FindIndex(e => string.Equals(e.Username?.Trim(), key, StringComparison.OrdinalIgnoreCase));
        var entry = new UserAccessKeyEntry
        {
            Username = key,
            AwsAccessKeyId = accessKeyId?.Trim() ?? "",
            AwsSecretAccessKey = secretAccessKey?.Trim() ?? ""
        };
        if (found >= 0)
            list[found] = entry;
        else
            list.Add(entry);
        SaveAll(list);
    }

    private sealed class UserAccessKeyEntry
    {
        public string? Username { get; set; }
        public string? AwsAccessKeyId { get; set; }
        public string? AwsSecretAccessKey { get; set; }
    }
}
