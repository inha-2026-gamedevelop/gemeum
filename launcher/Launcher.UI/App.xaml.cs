using System.Windows;
using System.Windows.Threading;

namespace Geumeum.Launcher;

public partial class App : Application
{
    private const string ARG_INSTALL_ROOT = "--install-root";
    private const string ARG_REPO         = "--repo";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        LauncherConfig config;
        try
        {
            config = LauncherConfig.LoadEmbedded();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"런처 설정을 읽지 못했다.\n{ex.Message}", "그믐 런처", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        LauncherSettings settings = LauncherSettings.Load(config.DefaultChannel);
        if (settings.MigrateLegacyServer(config.BaseUrl)) settings.Save();
        if (FindArg(e.Args, ARG_INSTALL_ROOT) is string root)
        {
            settings.InstallRoot = root;
        }

        // 배포 주소 바꿔치기는 개발 빌드에서만 연다. 릴리스 런처는 내장 주소만 믿는다
        string? repoOverride = null;
        string? accountOverride = null;
        bool gameAccounts = config.EnableGameAccounts;
#if DEBUG
        repoOverride = FindArg(e.Args, ARG_REPO);
        if (repoOverride != null && FindArg(e.Args, "--channel") is string localChannel)
        {
            if (localChannel is not ("internal" or "stable")) { Shutdown(1); return; }
            settings.Channel = localChannel;
        }
        accountOverride = FindArg(e.Args, "--account-api");
        gameAccounts |= e.Args.Contains("--account-game");
#endif

        if (gameAccounts && string.IsNullOrWhiteSpace(accountOverride ?? config.AccountApiUrl))
        {
            MessageBox.Show("게임 계정 연결에는 계정 API 주소가 필요합니다.", "그믐 런처");
            Shutdown(1);
            return;
        }
        var viewModel = new MainViewModel(config, settings, repoOverride, accountOverride, gameAccounts);
        var window    = new MainWindow(viewModel);
        MainWindow    = window;
        window.Show();
    }

    private static string? FindArg(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"예상하지 못한 오류가 났다.\n{e.Exception.Message}", "그믐 런처", MessageBoxButton.OK, MessageBoxImage.Error);
        System.Diagnostics.Trace.WriteLine(e.Exception.ToString());
        e.Handled = true;
    }
}
