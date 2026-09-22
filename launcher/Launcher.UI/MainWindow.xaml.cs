using System.ComponentModel;
using System.Windows;

namespace Geumeum.Launcher;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel  = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.State == LauncherState.Installing)
        {
            MessageBoxResult answer = MessageBox.Show(this,
                "설치 중이다. 닫으면 멈추고, 다음에 런처를 켜면 받은 부분부터 이어서 받는다.\n닫을까?",
                "그믐 런처", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _viewModel.CancelWork();
        }
        base.OnClosing(e);
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) => WindowState =
        WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // 초대 링크는 로그인, 계정 서버 상태와 무관하게 항상 열 수 있어야 한다
    private void OnDiscord(object sender, RoutedEventArgs e) => DiscordCommunity.OpenInvite(this);

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
