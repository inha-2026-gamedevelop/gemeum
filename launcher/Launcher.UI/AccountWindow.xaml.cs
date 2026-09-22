using System.Windows;

namespace Geumeum.Launcher;

public partial class AccountWindow : Window
{
    private readonly AccountViewModel _viewModel;

    public AccountWindow(AccountViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) =>
        {
            if (viewModel.IsSignedIn) await viewModel.RefreshProfileAsync();
            else LoginBox.Focus();
        };
    }

    private async void OnLogin(object sender, RoutedEventArgs e)
    {
        string password = LoginPassword.Password;
        ClearPasswords();
        await _viewModel.LoginAsync(password);
    }

    // 계정 발급 안내는 로그인 여부, API 연결 상태와 무관하게 항상 눌린다
    private void OnDiscord(object sender, RoutedEventArgs e) => DiscordCommunity.OpenInvite(this);

    private async void OnNickname(object sender, RoutedEventArgs e) => await _viewModel.ChangeNicknameAsync();
    private async void OnLogout(object sender, RoutedEventArgs e)
    {
        ClearPasswords();
        await _viewModel.LogoutAsync();
    }

    private async void OnPassword(object sender, RoutedEventArgs e)
    {
        string current = CurrentPassword.Password;
        string next = NewPassword.Password;
        string confirm = ConfirmPassword.Password;
        ClearPasswords();
        await _viewModel.ChangePasswordAsync(current, next, confirm);
    }

    private void ClearPasswords()
    {
        LoginPassword.Clear();
        CurrentPassword.Clear();
        NewPassword.Clear();
        ConfirmPassword.Clear();
    }

    protected override void OnClosed(EventArgs e)
    {
        ClearPasswords();
        _viewModel.CancelOperation();
        base.OnClosed(e);
    }
}
