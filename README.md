# DB → S3 身体図画像移行ツール（BodyImageMigrator）

BODYINFO テーブルの DRAWDATA / BGDATA（Base64/Data URL）を S3 に PNG としてアップロードする .NET 8 コンソールアプリです。

## 前提

- .NET 8 SDK
- SQL Server への接続（BODYINFO テーブルが存在する DB）
- AWS 認証情報（S3 書き込み権限）

## 環境変数（必須・推奨）

| 変数名 | 説明 |
|--------|------|
| `BODYIMAGE_DB_CONNECTION` | **必須**。SQL Server 接続文字列。例: `Server=ホスト;Database=DB名;User Id=ユーザー;Password=パスワード;TrustServerCertificate=true` |
| `AWS_REGION` | リージョン（未設定時は `ap-northeast-1`） |
| `AWS_ACCESS_KEY_ID` | AWS アクセスキー |
| `AWS_SECRET_ACCESS_KEY` | AWS シークレットアクセスキー |

※ 認証情報はコードに含めず、環境変数または実行時に設定してください。

## 実行画面（WPF）

GUI からパラメータを指定して実行できます。

```bash
cd src/BodyImageMigrator.Runner
dotnet run
```

またはビルド後:

```bash
dotnet run --project src/BodyImageMigrator.Runner
```

※ 環境変数（BODYIMAGE_DB_CONNECTION、AWS 関連）を事前に設定してください。

## ビルド

```bash
cd src/BodyImageMigrator
dotnet build
```

単一 exe で発行（Windows x64）:

```bash
dotnet publish -c Release
```

出力は `bin/Release/net8.0/win-x64/publish/BodyImageMigrator.exe` です。

## 実行例

```bash
# ドライラン（アップロードしない）
BodyImageMigrator.exe --dry-run

# 先頭 100 件のみ
BodyImageMigrator.exe --limit 100

# 上書きあり、施設コード絞り込み
BodyImageMigrator.exe --overwrite --istcd "001"

# 記録日時で範囲指定
BodyImageMigrator.exe --from 2024-01-01 --to 2024-12-31

# 削除済みを除外
BodyImageMigrator.exe --exclude-deleted

# ログ出力先を指定
BodyImageMigrator.exe --output "C:\MigrationLogs"
```

## オプション一覧

| オプション | 説明 |
|------------|------|
| `--dry-run` | アップロードせず、DB 読み取りとログ出力のみ |
| `--limit <n>` | 処理する最大件数（MEMONO 昇順の先頭から） |
| `--overwrite` | S3 に同一キーが既にある場合も上書き |
| `--istcd <コード>` | 施設コードで絞り込み |
| `--from <日付>` | 記録日時（RECDATE）の開始日 |
| `--to <日付>` | 記録日時（RECDATE）の終了日 |
| `--exclude-deleted` | 削除済み（DLTFLG=1）を対象から除外 |
| `--output <パス>` | ログ CSV の出力先ベース（未指定時は カレント/BodyImageMigration） |
| `--parallel <n>` | 並列数（1～10、デフォルト 5） |
| `--help`, `-h` | ヘルプ表示 |

## ログ出力先

- **デフォルト**: `カレントディレクトリ/BodyImageMigration/BodyImageMigrationLog/`
- **`--output` 指定時**: `指定パス/BodyImageMigrationLog/`

出力ファイル:

- `success_yyyyMMdd_HHmmss.csv` — アップロード成功（および既存のためスキップ）
- `failure_yyyyMMdd_HHmmss.csv` — アップロード失敗
- `skipped_yyyyMMdd_HHmmss.csv` — 空データなどでアップロードしなかった件（ログのみ）

## 移行先 S3

- バケット: `spc-crs-stg-apne1-s3-reports`
- リージョン: `ap-northeast-1`（東京）
- キー形式: `istcd={ISTCD}/ryono={RYONO}/bodyinfo/{MEMONO}_draw.png` / `{MEMONO}_bg.png`

## 仕様メモ

- DRAWDATA / BGDATA が NULL または空のレコードはスキップ（成功・失敗 CSV には出さず、skipped CSV にのみ出力）
- Data URL（`data:image/png;base64,...`）およびプレフィックスなしの Base64 を対応。保存は常に `.png`
- 削除フラグ（DLTFLG）はデフォルトで対象に含める。`--exclude-deleted` で除外可能

## ドキュメント

- [DBtoS3移行要望まとめ](docs/DBtoS3移行要望まとめ.md)
- [DBtoS3以降の実行方式について](docs/DBtoS3以降の実行方式について.md)
