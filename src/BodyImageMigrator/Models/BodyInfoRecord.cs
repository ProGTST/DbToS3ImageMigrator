namespace BodyImageMigrator.Models;

/// <summary>
/// BODYINFO テーブルの1行（移行に必要な列のみ）
/// </summary>
public class BodyInfoRecord
{
    public long MEMONO { get; set; }       // 身体図メモNo（S3キー用）
    public string? ISTCD { get; set; }     // 施設コード（S3パス用）
    public int RYONO { get; set; }         // 利用者No（S3パス用）
    public string? DRAWDATA { get; set; }  // 書き込みデータ（移行対象）
    public string? BGDATA { get; set; }    // 背景データ（移行対象）
    public bool DLTFLG { get; set; }       // 削除フラグ（--exclude-deleted用）
}
