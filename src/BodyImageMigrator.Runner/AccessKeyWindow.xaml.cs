using System.Windows;
using BodyImageMigrator;

namespace BodyImageMigrator.Runner;

public partial class AccessKeyWindow : Window
{
    private readonly string _username;

    public AccessKeyWindow(string username, Window owner)
    {
        InitializeComponent();
        _username = username?.Trim() ?? "";
        Owner = owner;
        TxtUserLabel.Text = _username;
        var existing = UserAccessKeyStore.GetByUsername(_username);
        if (existing.HasValue)
        {
            TxtAccessKeyId.Text = existing.Value.AccessKeyId;
            TxtSecretAccessKey.Password = existing.Value.SecretAccessKey;
        }
        TxtAccessKeyId.Focus();
    }

    private void BtnSet_Click(object sender, RoutedEventArgs e)
    {
        var accessKeyId = TxtAccessKeyId.Text?.Trim();
        var secretAccessKey = TxtSecretAccessKey.Password?.Trim();
        if (string.IsNullOrEmpty(accessKeyId) || string.IsNullOrEmpty(secretAccessKey))
        {
            System.Windows.MessageBox.Show("アクセスキーとシークレットアクセスキーを両方入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        UserAccessKeyStore.AddOrUpdate(_username, accessKeyId, secretAccessKey);

        if (Owner is MainWindow)
        {
            Close();
            return;
        }

        LoginWindow.SetEnvAndOpenMain(_username);
        Owner?.Close();
        Close();
    }
}
