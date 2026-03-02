========================================
BodyImageMigrator 実行ファイル
========================================

【Console】コンソール版（コマンドライン）
  フォルダ: Console\
  実行ファイル: BodyImageMigrator.exe

  使い方:
    1. 環境変数を設定（BODYIMAGE_DB_CONNECTION, AWS_REGION, AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY）
    2. コマンドプロンプトまたは PowerShell で:
       BodyImageMigrator.exe --dry-run
       BodyImageMigrator.exe --limit 100
       BodyImageMigrator.exe --help

【Runner】GUI版（実行画面）
  フォルダ: Runner\
  実行ファイル: BodyImageMigrator.Runner.exe

  使い方:
    1. 環境変数を設定（上記と同じ）
    2. BodyImageMigrator.Runner.exe をダブルクリックまたはコマンドで起動

----------------------------------------
配布時: Console または Runner フォルダごとコピーして使用してください。
       .exe と同じフォルダに DLL 等が必要です。
----------------------------------------
