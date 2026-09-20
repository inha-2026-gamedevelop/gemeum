using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Geumeum.Patch.Client;
using Microsoft.Win32;

namespace Geumeum.Launcher;

public partial class SettingsWindow : Window
{
    private readonly string _logsDir;

    public SettingsWindow(string installRoot, string channel, string serverUrl, string builtInServerUrl, string logsDir)
    {
        InitializeComponent();
        _logsDir       = logsDir;
        InstallRoot    = installRoot;
        Channel        = channel;
        ServerUrl      = serverUrl;
        RootBox.Text   = installRoot;
        ServerHint.Text = serverUrl.Length > 0 || builtInServerUrl.Length > 0
            ? "지정된 배포 서버에서 업데이트를 받습니다."
            : "업데이트 서버가 설정되지 않았습니다. 배포 담당자에게 문의하세요.";
        ChannelBox.SelectedItem = ChannelBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == channel) ?? ChannelBox.Items[0];
    }

    public string InstallRoot     { get; private set; }
    public string Channel         { get; private set; }
    public string ServerUrl       { get; private set; }
    public bool   RepairRequested { get; private set; }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnDragWindow(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
        {
            DragMove();
        }
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "설치할 폴더", InitialDirectory = Directory.Exists(RootBox.Text) ? RootBox.Text : "" };
        if (dialog.ShowDialog(this) == true)
        {
            RootBox.Text = Path.Combine(dialog.FolderName, "Geumeum");
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!TryApply())
        {
            return;
        }
        DialogResult = true;
    }

    private void OnRepair(object sender, RoutedEventArgs e)
    {
        if (!TryApply())
        {
            return;
        }
        RepairRequested = true;
        DialogResult    = true;
    }

    private bool TryApply()
    {
        string root;
        try
        {
            root = Path.GetFullPath(RootBox.Text.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            MessageBox.Show(this, $"경로가 올바르지 않다: {ex.Message}", "설정", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (LocalVolume.Problem(root) is string problem)
        {
            MessageBox.Show(this, problem, "설정", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        InstallRoot = root;
        Channel     = (string)((ComboBoxItem)ChannelBox.SelectedItem).Tag;
        return true;
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_logsDir);
        Process.Start(new ProcessStartInfo(_logsDir) { UseShellExecute = true })?.Dispose();
    }
}
