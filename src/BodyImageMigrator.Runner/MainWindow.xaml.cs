using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Forms;
using BodyImageMigrator;
namespace BodyImageMigrator.Runner;

public partial class MainWindow : Window
{
    private const string CrntDirPlaceholder = "{CrntDir}";

    private CancellationTokenSource? _cts;
    private readonly object _logLock = new();
    private StringBuilder _logBuilder = new();

    /// <summary>
    /// {CrntDir} の実体。exe の親フォルダ（例: bin\Debug または bin\Release）を指す。
    /// 参照ダイアログの初期位置と同一にし、選択フォルダがこの直下なら {CrntDir}\相対 で表示する。
    /// </summary>
    private static string GetCrntDirBase()
    {
        var baseDir = AppContext.BaseDirectory;
        var trimmed = baseDir.TrimEnd(Path.DirectorySeparatorChar, '/');
        var parent = Path.GetDirectoryName(trimmed);
        return string.IsNullOrEmpty(parent) ? Path.GetFullPath(trimmed) : Path.GetFullPath(parent);
    }

    public MainWindow()
    {
        InitializeComponent();
        // 未入力のときは publish\BodyImageMigration を使用（BuildOptions で null → GetBaseDirectory が共通ベースを返す）
        TxtOutputPath.Text = "";
        // 並列数は 1～10 のみ選択可能
        for (var i = 1; i <= 10; i++)
            CmbParallel.Items.Add(i);
        CmbParallel.SelectedIndex = 4; // 5
        // 最大件数: 貼り付け時も数字のみに制限
        System.Windows.DataObject.AddPastingHandler(TxtLimit, OnTxtLimitPaste);
    }

    private void OnTxtLimitPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.SourceDataObject.GetDataPresent(System.Windows.DataFormats.Text))
        {
            var text = (e.SourceDataObject.GetData(System.Windows.DataFormats.Text) as string) ?? "";
            var digitsOnly = new string(text.Where(char.IsDigit).ToArray());
            if (digitsOnly != text)
            {
                e.CancelCommand();
                TxtLimit.SelectedText = digitsOnly;
            }
        }
    }

    /// <summary>ダイアログに渡すパスを正規化（バックスラッシュ・末尾なし）</summary>
    private static string NormalizePathForDialog(string path)
    {
        var full = Path.GetFullPath(path.Trim().Replace('\\', Path.DirectorySeparatorChar));
        return full.TrimEnd(Path.DirectorySeparatorChar, '\\');
    }

    private static string ExpandCrntDir(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path ?? "";
        var expanded = path.Trim().Replace(CrntDirPlaceholder, GetCrntDirBase(), StringComparison.OrdinalIgnoreCase);
        return expanded.Replace('\\', Path.DirectorySeparatorChar);
    }

    /// <summary>表示用パスは末尾に \ を付ける</summary>
    private static string EnsureTrailingBackslash(string displayPath)
    {
        if (string.IsNullOrEmpty(displayPath)) return displayPath;
        var t = displayPath.Trim();
        var sep = Path.DirectorySeparatorChar;
        if (t.EndsWith(sep)) return t;
        if (t.EndsWith('\\')) return t.TrimEnd('\\') + sep;
        return t + sep;
    }

    private static string ToDisplayPath(string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath)) return selectedPath ?? "";
        var baseDir = GetCrntDirBase();
        var full = Path.GetFullPath(selectedPath.Trim().Replace('/', Path.DirectorySeparatorChar));
        baseDir = baseDir.TrimEnd(Path.DirectorySeparatorChar, '/');
        full = full.TrimEnd(Path.DirectorySeparatorChar, '/');
        var baseSep = baseDir + Path.DirectorySeparatorChar;
        if (full.Equals(baseDir, StringComparison.OrdinalIgnoreCase))
            return EnsureTrailingBackslash(CrntDirPlaceholder);
        if (full.StartsWith(baseSep, StringComparison.OrdinalIgnoreCase))
        {
            var suffix = full.Substring(baseSep.Length);
            var result = string.IsNullOrEmpty(suffix) ? CrntDirPlaceholder : $"{CrntDirPlaceholder}{Path.DirectorySeparatorChar}{suffix}";
            return EnsureTrailingBackslash(result);
        }
        return EnsureTrailingBackslash(full);
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "ログ出力先フォルダを選択",
            UseDescriptionForTitle = true
        };
        var current = TxtOutputPath.Text?.Trim();
        if (!string.IsNullOrEmpty(current))
        {
            var expanded = ExpandCrntDir(current).Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(expanded.TrimEnd(Path.DirectorySeparatorChar, '/'));
            if (Directory.Exists(fullPath))
                dialog.SelectedPath = fullPath + Path.DirectorySeparatorChar;
            else
            {
                var parent = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    dialog.SelectedPath = parent + Path.DirectorySeparatorChar;
            }
        }
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedPath))
        {
            TxtOutputPath.Text = ToDisplayPath(dialog.SelectedPath);
        }
    }

    private void BtnAccessKey_Click(object sender, RoutedEventArgs e)
    {
        var username = LoginWindow.CurrentUsername ?? "";
        var win = new AccessKeyWindow(username, this);
        win.ShowDialog();
    }

    /// <summary>最大件数: 整数（0～9）のみ入力可能</summary>
    private void TxtLimit_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !Regex.IsMatch(e.Text, @"^\d+$");
    }

    private MigratorOptions BuildOptions()
    {
        var outputPathRaw = TxtOutputPath.Text?.Trim();
        var outputPath = string.IsNullOrWhiteSpace(outputPathRaw) ? null : ExpandCrntDir(outputPathRaw).Trim();
        if (string.IsNullOrEmpty(outputPath)) outputPath = null;

        var opts = new MigratorOptions
        {
            DryRun = ChkDryRun.IsChecked == true,
            Overwrite = ChkOverwrite.IsChecked == true,
            ExcludeDeleted = ChkExcludeDeleted.IsChecked == true,
            Istcd = string.IsNullOrWhiteSpace(TxtIstcd.Text) ? null : TxtIstcd.Text.Trim(),
            OutputPath = outputPath,
            Parallel = 5
        };

        // 最大件数: 未入力＝無制限（null）、0以上＝その件数で制限
        var limitText = TxtLimit.Text?.Trim();
        opts.Limit = string.IsNullOrEmpty(limitText)
            ? null
            : (int.TryParse(limitText, out var limit) && limit >= 0 ? (int?)limit : null);

        // 並列数: 1～10 の ComboBox から取得（未選択時は 5）
        opts.Parallel = CmbParallel.SelectedItem is int p && p >= 1 && p <= 10 ? p : 5;

        if (DpFrom.SelectedDate.HasValue)
            opts.From = DpFrom.SelectedDate.Value.Date;

        if (DpTo.SelectedDate.HasValue)
            opts.To = DpTo.SelectedDate.Value.Date;

        var username = LoginWindow.CurrentUsername?.Trim();
        if (!string.IsNullOrEmpty(username))
        {
            var keys = UserAccessKeyStore.GetByUsername(username);
            if (keys.HasValue)
            {
                opts.AwsAccessKeyId = keys.Value.AccessKeyId;
                opts.AwsSecretAccessKey = keys.Value.SecretAccessKey;
            }
        }

        return opts;
    }

    private async void BtnRun_Click(object sender, RoutedEventArgs e)
    {
        BtnRun.IsEnabled = false;
        BtnStop.IsEnabled = true;
        _cts = new CancellationTokenSource();
        _logBuilder = new StringBuilder();

        var opts = BuildOptions();
        AppendLog($"開始: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        AppendLog($"パラメータ: {opts.ToParametersString()}");
        AppendLog("");

        try
        {
            var originalOut = Console.Out;
            var originalErr = Console.Error;
            var capturingWriter = new ActionTextWriter(s =>
            {
                lock (_logLock)
                {
                    _logBuilder.Append(s);
                    Dispatcher.Invoke(() => TxtLog.Text = _logBuilder.ToString());
                }
            });
            Console.SetOut(capturingWriter);
            Console.SetError(capturingWriter);

            try
            {
                var service = new MigrationService(opts);
                await service.RunAsync(_cts.Token).ConfigureAwait(true);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }

            AppendLog("");
            AppendLog($"完了: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }
        catch (OperationCanceledException)
        {
            AppendLog("");
            AppendLog("【中止】ユーザーによりキャンセルされました。");
        }
        catch (Exception ex)
        {
            AppendLog("");
            AppendLog($"エラー: {ex.Message}");
        }
        finally
        {
            BtnRun.IsEnabled = true;
            BtnStop.IsEnabled = false;
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

    private void AppendLog(string text)
    {
        lock (_logLock)
        {
            _logBuilder.AppendLine(text);
            TxtLog.Text = _logBuilder.ToString();
        }
    }

    private class ActionTextWriter : TextWriter
    {
        private readonly Action<string> _action;

        public ActionTextWriter(Action<string> action) => _action = action;

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => _action(value.ToString());
        public override void Write(string? value)
        {
            if (value != null) _action(value);
        }
    }
}
