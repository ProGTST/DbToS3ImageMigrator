using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using BodyImageMigrator;
namespace BodyImageMigrator.Runner;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _cts;
    private readonly object _logLock = new();
    private StringBuilder _logBuilder = new();

    public MainWindow()
    {
        InitializeComponent();
        // 並列数は 1～10 のみ選択可能
        for (var i = 1; i <= 10; i++)
            CmbParallel.Items.Add(i);
        CmbParallel.SelectedIndex = 4; // 5
        // 最大件数・利用者No: 貼り付け時も数字のみに制限
        System.Windows.DataObject.AddPastingHandler(TxtLimit, OnTxtLimitPaste);
        System.Windows.DataObject.AddPastingHandler(TxtRyono, OnTxtRyonoPaste);
    }

    private void OnTxtLimitPaste(object sender, DataObjectPastingEventArgs e)
    {
        PasteDigitsOnly(sender, e, TxtLimit);
    }

    private void OnTxtRyonoPaste(object sender, DataObjectPastingEventArgs e)
    {
        PasteDigitsOnly(sender, e, TxtRyono);
    }

    private static void PasteDigitsOnly(object sender, DataObjectPastingEventArgs e, System.Windows.Controls.TextBox target)
    {
        if (e.SourceDataObject.GetDataPresent(System.Windows.DataFormats.Text))
        {
            var text = (e.SourceDataObject.GetData(System.Windows.DataFormats.Text) as string) ?? "";
            var digitsOnly = new string(text.Where(char.IsDigit).ToArray());
            if (digitsOnly != text)
            {
                e.CancelCommand();
                target.SelectedText = digitsOnly;
            }
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

    /// <summary>利用者No: 整数のみ入力可能</summary>
    private void TxtRyono_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !Regex.IsMatch(e.Text, @"^\d+$");
    }

    private MigratorOptions BuildOptions()
    {
        var opts = new MigratorOptions
        {
            DryRun = ChkDryRun.IsChecked == true,
            Overwrite = ChkOverwrite.IsChecked == true,
            ExcludeDeleted = ChkExcludeDeleted.IsChecked == true,
            Istcd = string.IsNullOrWhiteSpace(TxtIstcd.Text) ? null : TxtIstcd.Text.Trim(),
            Ryono = int.TryParse(TxtRyono.Text?.Trim(), out var ryono) && ryono >= 0 ? ryono : null,
            OutputPath = null, // 常に publish\BodyImageMigration を使用
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
