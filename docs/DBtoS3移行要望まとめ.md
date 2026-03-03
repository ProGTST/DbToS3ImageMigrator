# DB→S3 身体図画像移行 要望まとめ

## 1. 背景・目的

| 項目 | 現システム | 新システム |
|------|------------|------------|
| 身体図画像の保持形式 | DBにBase64形式で保持 | S3で画像ファイルとして管理 |

**要望**: 過去の画像を **DB → S3** に移行する方法の検討

- 本番稼働**前**のデータ移行時のみ使用するツール・処理でよい
- 本格的な画面は不要

---

## 2. 移行先

- **リージョン**: ap-northeast-1（東京）
- **S3バケット**: `spc-crs-stg-apne1-s3-reports`
- コンソール: [S3 オブジェクト一覧](https://ap-northeast-1.console.aws.amazon.com/s3/buckets/spc-crs-stg-apne1-s3-reports?region=ap-northeast-1&tab=objects)

---

## 3. フォルダ構成

```
istcd={ISTCD}/
└── ryono={RYONO}/
    └── bodyinfo/
        ├── {MEMONO}_bg.png    … 背景データ（BGDATA）
        └── {MEMONO}_draw.png  … 書き込みデータ（DRAWDATA）
```

- **ISTCD**: 施設コード（BODYINFO.ISTCD）
- **RYONO**: 利用者No（BODYINFO.RYONO）
- **MEMONO**: 身体図メモNo（BODYINFO.MEMONO）
- ファイルは `.png` で保存する

## 4. 対象テーブル：BODYINFO（身体図）

| No | 論理名     | カラム名   | 型                 | 備考        |
|----|------------|------------|--------------------|-------------|
| 1  | 身体図メモNo | MEMONO     | bigint identity    | PK          |
| 2  | 施設コード   | ISTCD      | varchar(5)         |             |
| 3  | 利用者No     | RYONO      | int                | Not Null    |
| 4  | 担当者No     | TNTNO      | int                |             |
| 5  | 記録日時     | RECDATE    | datetime           |             |
| 6  | タイトル     | TITLE      | nvarchar(20)       |             |
| **7**  | **書き込みデータ** | **DRAWDATA** | **varchar(max)** | **移行対象** |
| **8**  | **背景データ**   | **BGDATA**   | **varchar(max)** | **移行対象** |
| 9  | メモ内容     | MEMO       | nvarchar(1000)     |             |
| 10 | ロックフラグ | LOCKFLG    | bit                | Not Null    |
| 11 | 更新回数     | UPDCNT     | int                | Not Null    |
| 12 | 登録日時     | INSDATE    | datetime           |             |
| 13 | 登録プログラムID | INSPGID   | varchar(20)        |             |
| 14 | 登録プログラム名 | INSPGNAME | nvarchar(50)       |             |
| 15 | 登録ユーザNo   | INSUSERNO | int                | Not Null    |
| 16 | 登録ログインNo  | INSLOGINNO | bigint            | Not Null    |
| 17 | 登録ログNo     | INSLOGNO   | bigint            | Not Null    |
| 18 | 更新日時     | UPDDATE    | datetime           |             |
| 19 | 更新プログラムID | UPDPGID   | varchar(20)        |             |
| 20 | 更新プログラム名 | UPDPGNAME | nvarchar(50)       |             |
| 21 | 更新ユーザNo   | UPDUSERNO | int                | Not Null    |
| 22 | 更新ログインNo  | UPDLOGINNO | bigint            | Not Null    |
| 23 | 更新ログNo     | UPDLOGNO   | bigint            | Not Null    |
| 24 | 削除フラグ   | DLTFLG     | bit                | Not Null    |

---

## 5. データ移行対象カラム

| カラム名   | 論理名       | 型           | 備考 |
|------------|--------------|--------------|------|
| **DRAWDATA** | 書き込みデータ | varchar(max) | → `istcd={ISTCD}/ryono={RYONO}/bodyinfo/{MEMONO}_draw.png` としてS3に保存 |
| **BGDATA**   | 背景データ     | varchar(max) | → `istcd={ISTCD}/ryono={RYONO}/bodyinfo/{MEMONO}_bg.png` としてS3に保存 |

- 各カラムにはBase64（またはData URL形式）の画像データが格納されている想定
- 移行時は PNG 形式で保存する（元が他形式の場合は変換する想定）

---

## 6. 実装方式

**方針**: **ローカル実行コンソールバッチ（.NET）＋ AWS SDK**

- 方式の比較・選定理由は [DBtoS3以降の実行方式について.md](./DBtoS3以降の実行方式について.md) を参照
- 本番稼働前の**一時的な移行**であるため、運用のしやすさ（実行手順の明確さ、ロールバックのしやすさ）を重視し、上記方式を採用

### 実装イメージ（.NET を採用する場合）

- .NET 8 Console ＋ AWS SDK for .NET
- SQL接続（Dapper等）、並列数制御あり
- ログ出力（成功／失敗CSV）
- オプション: `--dry-run` / `--limit` / `--overwrite`
- 単一exe化可能（single-file publish）で環境依存を抑える

---

## 7. 補足・前提（実装で反映した仕様）

- **施設番号**: S3 パスは `istcd={ISTCD}` の形式で実装済み
- **拡張子・Data URL**: Data URL の場合は `.png` で保存。プレフィックスがない場合は常に `.png` として扱う
- **空データ**: DRAWDATA / BGDATA が NULL または空のレコードはスキップ（成功・失敗CSVには出さず、skipped CSV にのみ出力）
- **認証**: DB 接続は環境変数 `BODYIMAGE_DB_CONNECTION`、AWS は `AWS_REGION` / `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` で指定（コードに認証情報は含めない）
- **削除フラグ**: デフォルトは削除済み（DLTFLG=1）も対象に含める。`--exclude-deleted` で除外可能
- **ログCSV**: デフォルトは `カレント/BodyImageMigration/BodyImageMigrationLog/`。`--output` でベースパス指定時は `指定パス/BodyImageMigrationLog/`。ファイル名は `success_yyyyMMdd_HHmmss.csv` / `failure_...` / `skipped_...`
- **並列数**: デフォルト 5、上限 10（`--parallel` で指定）
- **絞り込み**: `--limit`（MEMONO 昇順）、`--istcd`、`--from` / `--to`（RECDATE）で指定可能

ツールのビルド・実行方法はリポジトリ直下の [README.md](../README.md) を参照。

---

## 8. ドキュメント更新履歴

| 日付       | 内容 |
|------------|------|
| 2025-02-26 | 初版作成（要望まとめ） |
| 2025-02-26 | 実装方式を「ローカル実行コンソールバッチ（.NET / Node）＋ AWS SDK」に決定し、セクション6を修正 |
| 2025-02-26 | .NET (C#) でツール実装。セクション7を実装で反映した仕様に更新 |
