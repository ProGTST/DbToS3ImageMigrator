namespace BodyImageMigrator;

/// <summary>
/// Data URL または Base64 文字列からバイナリを取得する。
/// 拡張子は常に .png として扱う（仕様）。
/// </summary>
public static class DataUrlHelper
{
    private const string DataUrlPrefix = "data:";
    private const string Base64Suffix = ";base64,";

    /// <summary>
    /// Data URL または Base64 文字列から画像バイト列を取得する。
    /// プレフィックスがない場合は文字列全体を Base64 としてデコードし、.png として扱う。
    /// </summary>
    /// <param name="value">NULL または空の場合は null を返す</param>
    /// <returns>デコードしたバイト列。失敗時は null</returns>
    public static byte[]? TryDecodeToImageBytes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            return null;

        string base64String;

        if (trimmed.StartsWith(DataUrlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var base64Index = trimmed.IndexOf(Base64Suffix, StringComparison.OrdinalIgnoreCase);
            if (base64Index < 0)
                return null;
            base64String = trimmed[(base64Index + Base64Suffix.Length)..].Trim();
        }
        else
        {
            base64String = trimmed;
        }

        if (base64String.Length == 0)
            return null;

        try
        {
            return Convert.FromBase64String(base64String);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
