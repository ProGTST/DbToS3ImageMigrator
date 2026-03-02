using System.Windows;
using System.Windows.Input;
using BodyImageMigrator;

namespace BodyImageMigrator.Runner;

public partial class LoginWindow : Window
{
    /// <summary>ログイン済みユーザー名。実行画面からアクセスキー設定を開くときに使用する。</summary>
    public static string? CurrentUsername { get; set; }

    public LoginWindow()
    {
        InitializeComponent();
        TxtUsername.Focus();
        TxtUsername.KeyDown += (_, e) => { if (e.Key == Key.Enter) BtnLogin_Click(null!, null!); };
    }

    private void BtnLogin_Click(object sender, RoutedEventArgs e)
    {
        var username = TxtUsername.Text?.Trim();
        if (string.IsNullOrEmpty(username))
        {
            System.Windows.MessageBox.Show("ユーザー名を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var keys = UserAccessKeyStore.GetByUsername(username);
        if (keys.HasValue)
        {
            CurrentUsername = username;
            SetEnvAndOpenMain(username);
            Close();
            return;
        }

        var accessKeyWindow = new AccessKeyWindow(username, this);
        accessKeyWindow.Owner = this;
        accessKeyWindow.ShowDialog();
    }

    internal static void SetEnvAndOpenMain(string? username)
    {
        if (!string.IsNullOrEmpty(username)) CurrentUsername = username;
        var main = new MainWindow();
        System.Windows.Application.Current.MainWindow = main;
        main.Show();
    }
}
