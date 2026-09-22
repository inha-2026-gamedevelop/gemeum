using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Geumeum.Accounts;
using Geumeum.Launcher;
using Geumeum.Patch;
using Geumeum.Patch.Client;
using Xunit;

namespace Geumeum.AccountTests;

public sealed class LiveAccountFactAttribute : FactAttribute
{
    public LiveAccountFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_PASSWORD") == null)
            Skip = "Run backend/scripts/smoke_local.py --launcher with the local API running.";
    }
}

public sealed class LiveAccountTests
{
    [LiveAccountFact]
    public async Task LocalApiAndWpfAccountFlow()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new App();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            _ = Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
            {
                try { await ExerciseAsync(); completion.SetResult(); }
                catch (Exception error) { completion.SetException(error); }
                finally { app.Shutdown(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task;
        thread.Join();
    }

    private static async Task ExerciseAsync()
    {
        string origin = Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_ORIGIN")!;
        string login = Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_LOGIN")!;
        string password = Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_PASSWORD")!;
        string artifacts = Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_ARTIFACTS")!;
        string state = Path.Combine(artifacts, "credentials-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifacts);
        try
        {
            using (var main = new MainViewModel(LauncherConfig.LoadEmbedded(),
                new LauncherSettings { InstallRoot = Path.Combine(state, "install") }, null, origin, gameAccounts: true,
                accountStateDirectory: Path.Combine(state, "main")))
            {
                typeof(MainViewModel).GetProperty(nameof(MainViewModel.State))!.SetValue(main, LauncherState.Ready);
                Assert.False(main.IsPrimaryEnabled);
                Assert.False(main.PrimaryCommand.CanExecute(null));
                main.Account!.LoginId = login;
                var pending = main.Account.LoginAsync(password);
                Assert.False(main.IsPrimaryEnabled);
                await pending;
                Assert.True(main.IsPrimaryEnabled, main.Account.Message);
                await main.Account.LogoutAsync();
                Assert.False(main.IsPrimaryEnabled);
            }
            if (Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_REPO") is string repo)
            {
                var layout = new InstallLayout(Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_INSTALL")!);
                using var remote = new FolderRemoteRepository(repo);
                var installer = new Installer(layout, remote, LauncherConfig.LoadEmbedded().TrustedKeys,
                    "internal", new Version(1, 2, 8), new PatchLog(layout.LogsDir));
                using var installTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                var check = await installer.CheckAsync(installTimeout.Token);
                await installer.InstallAsync(check, null, installTimeout.Token);
                Assert.True(installer.GetLocalStatus().IsInstalled);
                File.WriteAllText(Path.Combine(artifacts, "local-install.txt"), "PASS: signed local game installed by launcher patch engine");
            }
            using (var api = new AccountApi(origin, true))
            {
                TokenReply parent = await api.LoginAsync(login, password);
                TokenReply child = await api.CreateGameSessionAsync(parent.AccessToken);
                Assert.Equal("game", child.SessionKind);
                Assert.Equal(parent.Account.AccountId, (await api.MeAsync(child.AccessToken)).AccountId);
                await api.LogoutAsync(parent.AccessToken);
                var denied = await Assert.ThrowsAsync<AccountException>(() => api.MeAsync(child.AccessToken));
                Assert.Equal(HttpStatusCode.Unauthorized, denied.Status);
            }
            using (var vm = new AccountViewModel(origin, true, state))
            {
                Capture(vm, Path.Combine(artifacts, "account-login.png"));
                vm.LoginId = login;
                await vm.LoginAsync("wrong-password-value");
                Assert.True(vm.IsError);
                Assert.False(vm.IsSignedIn);
                vm.RememberLogin = true;
                await vm.LoginAsync(password);
                Assert.True(vm.IsSignedIn, vm.Message);
                vm.Nickname = "로컬검증";
                await vm.ChangeNicknameAsync();
                Assert.False(vm.IsError, vm.Message);
                Assert.Equal("로컬검증", vm.Nickname);
                Capture(vm, Path.Combine(artifacts, "account-signed-in.png"));
                var rooms = await vm.RoomsAsync(CancellationToken.None);
                if (Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_AUTO_ROOM") == "1")
                {
                    var autoRoom = new RoomWindow(vm, rooms.BuildId);
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                    int step = 0;
                    var until = DateTime.UtcNow.AddSeconds(100);
                    timer.Tick += (_, _) =>
                    {
                        if (DateTime.UtcNow > until) { timer.Stop(); autoRoom.Close(); return; }
                        if (autoRoom.IsLoaded && ((FrameworkElement)autoRoom.FindName("Actions")).IsEnabled)
                        {
                            if (step == 0) { step++; ((Button)autoRoom.FindName("CreateRoomButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                            else if (step == 1 && autoRoom.CurrentRoom?.State == "waiting")
                            {
                                Assert.Null(autoRoom.SelectedRoom);
                                string current = autoRoom.CurrentRoom.RoomId;
                                var browser = (ListBox)autoRoom.FindName("Rooms");
                                browser.ItemsSource = new[] { autoRoom.CurrentRoom, new AccountRoom { RoomId = Guid.NewGuid().ToString(), Name = "다른 파티 둘러보기", State = "waiting", OwnerNickname = "다른방장" } };
                                browser.SelectedIndex = 1;
                                Assert.Equal(current, autoRoom.CurrentRoom.RoomId);
                                Assert.Equal(Visibility.Visible, ((FrameworkElement)autoRoom.FindName("PartyPanel")).Visibility);
                                Assert.Equal(autoRoom.CurrentRoom.Name, ((TextBlock)autoRoom.FindName("SelectedTitle")).Text);
                                SaveVisibleRoom(autoRoom, Path.Combine(artifacts, "launcher-party-browse.png"));
                                step++;
                                ((Button)autoRoom.FindName("BotButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            }
                            else if (step == 2) { step++; ((Button)((StackPanel)autoRoom.FindName("WeaponButtons")).Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                            else if (step == 3) { step++; ((Button)autoRoom.FindName("ReadyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                            else if (step == 4)
                            {
                                Assert.True(((Button)autoRoom.FindName("StartButton")).IsEnabled);
                                Assert.Null(autoRoom.SelectedRoom);
                                SaveVisibleRoom(autoRoom, Path.Combine(artifacts, "launcher-party-ready.png"));
                                step++;
                                ((Button)autoRoom.FindName("StartButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            }
                        }
                    };
                    timer.Start();
                    bool? joined;
                    try { joined = autoRoom.ShowDialog(); }
                    finally { timer.Stop(); }
                    Assert.True(joined);
                    Assert.Equal(vm.AccountId, autoRoom.SelectedRoom!.OwnerId);
                    Assert.Equal(autoRoom.SelectedRoom.Hosting == "listen" ? "hosting" : "ready", autoRoom.SelectedRoom.State);
                    await vm.CloseRoomAsync(autoRoom.SelectedRoom.RoomId, CancellationToken.None);
                    File.WriteAllText(Path.Combine(artifacts, "auto-room.txt"), "PASS: launcher party creation/browse isolation/weapon/bots/ready/start; game selection occurred only after departure");
                }
                var roomWindow = new RoomWindow(vm, rooms.BuildId);
                ((ListBox)roomWindow.FindName("Rooms")).ItemsSource = new[] {
                    new AccountRoom { Name = "그믐 원정대", State = "ready", OwnerId = vm.AccountId!, OwnerNickname = "로컬검증", Members = new() {
                        new RoomMember { AccountId = vm.AccountId!, Nickname = "로컬검증", IsOwner = true, Connected = true },
                        new RoomMember { AccountId = "2", Nickname = "신궁", Connected = true },
                        new RoomMember { AccountId = "3", Nickname = "무녀", Connected = false }
                    } },
                    new AccountRoom { Name = "새로운 원정", State = "starting", OwnerNickname = "새방장" }
                };
                ((ListBox)roomWindow.FindName("Rooms")).SelectedIndex = 0;
                Assert.Null(roomWindow.CurrentRoom);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)roomWindow.FindName("PartyPanel")).Visibility);
                Assert.Equal(0, ((ColumnDefinition)roomWindow.FindName("PartyColumn")).Width.Value);
                CaptureWindow(roomWindow, vm, Path.Combine(artifacts, "account-rooms.png"), 1184, 860);
            }
            using (var restored = new AccountViewModel(origin, true, state))
            {
                await restored.RestoreAsync();
                Assert.True(restored.IsSignedIn, restored.Message);
                Assert.True(restored.RememberLogin);
                await restored.ChangePasswordAsync(password, "New" + password, "New" + password);
                Assert.False(restored.IsError, restored.Message);
                Assert.False(restored.IsSignedIn);
                await restored.LoginAsync("New" + password);
                Assert.True(restored.IsSignedIn, restored.Message);
                await restored.LogoutAsync();
                Assert.False(restored.IsSignedIn);
                Assert.False(restored.IsError, restored.Message);
            }
            using var empty = new AccountViewModel(origin, true, state);
            await empty.RestoreAsync();
            Assert.False(empty.IsSignedIn);
        }
        finally { if (Directory.Exists(state)) Directory.Delete(state, true); }
    }

    private static void Capture(AccountViewModel vm, string path)
    {
        CaptureWindow(new AccountWindow(vm), vm, path, 500, 650);
    }

    private static void CaptureWindow(Window window, AccountViewModel vm, string path, int width, int height)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        content.DataContext = vm;
        var surface = new Border { Background = window.Background, Child = content };
        surface.Resources = window.Resources;
        TextElement.SetForeground(surface, window.Foreground);
        TextElement.SetFontFamily(surface, window.FontFamily);
        surface.Measure(new Size(width, height));
        surface.Arrange(new Rect(0, 0, width, height));
        surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
        window.Close();
    }

    private static void SaveVisibleRoom(Window window, string path)
    {
        var element = (FrameworkElement)window.Content;
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}
