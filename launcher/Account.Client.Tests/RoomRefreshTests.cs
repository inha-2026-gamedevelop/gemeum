using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Threading;
using Geumeum.Accounts;
using Geumeum.Launcher;
using Xunit;

namespace Geumeum.AccountTests;

public sealed class RoomRefreshTests
{
    [Fact]
    public async Task TimerKeepsPartyAliveWithoutReloadingRoomList()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            _ = Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
            {
                try { await Exercise(); done.SetResult(); }
                catch (Exception ex) { done.SetException(ex); }
                finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(25)); thread.Join();
    }

    private static async Task Exercise()
    {
        int lists = 0, pulses = 0;
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var room = new AccountRoom { RoomId = "00000000-0000-0000-0000-000000000123", BuildId = "test", State = "waiting", Name = "Test", OwnerId = "123", Hosting = "listen",
            Members = new() { new RoomMember { AccountId = "123", Nickname = "Tester", IsOwner = true } } };
        HttpResponseMessage Reply(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body, json)) };
        using var session = new AccountSession(new AccountApi("https://accounts.example.test", handler: new Handler(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("login")) return Reply(new TokenReply { AccessToken = new string('a',43), RefreshToken = new string('b',43),
                TokenType = "Bearer", ExpiresIn = 900, SessionKind = "launcher", SessionExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                Account = new AccountProfile { AccountId = "123", LoginId = "tester", Nickname = "Tester", Status = "active" } });
            if (path.EndsWith("lobby")) { ++pulses; return Reply(room); }
            if (request.Method == HttpMethod.Get && path.EndsWith("rooms")) { ++lists; return Reply(new RoomList { BuildId = "test" }); }
            throw new InvalidOperationException(path);
        })), new MemoryStore());
        using var vm = new AccountViewModel("https://accounts.example.test", false, System.IO.Path.GetTempPath());
        var sessionField = typeof(AccountViewModel).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((AccountSession?)sessionField.GetValue(vm))?.Dispose(); sessionField.SetValue(vm, session);
        vm.LoginId = "tester"; await vm.LoginAsync("test-password"); Assert.True(vm.IsSignedIn, vm.Message);
        var window = new RoomWindow(vm, "test");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var timer = (DispatcherTimer)typeof(RoomWindow).GetField("_partyPulse", flags)!.GetValue(window)!;
        async Task Refresh() => await (Task)typeof(RoomWindow).GetMethod("RefreshAsync", flags)!.Invoke(window, null)!;
        void Update() => typeof(RoomWindow).GetMethod("UpdateSelection", flags)!.Invoke(window, null);
        try
        {
            Assert.False(timer.IsEnabled); await Refresh();
            await Task.Delay(2200); Assert.Equal(1, lists); Assert.Equal(0, pulses);
            typeof(RoomWindow).GetField("_current", flags)!.SetValue(window, room); Update();
            Assert.True(timer.IsEnabled);
            var list = (ListBox)window.FindName("Rooms"); object source = list.ItemsSource;
            await Task.Delay(4500);
            Assert.InRange(pulses, 2, 3); Assert.Equal(1, lists); Assert.Same(source, list.ItemsSource);
            await Refresh(); Assert.Equal(2, lists);
            typeof(RoomWindow).GetField("_current", flags)!.SetValue(window, null); Update();
            Assert.False(timer.IsEnabled);
        }
        finally { timer.Stop(); window.Close(); }
    }

    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(send(request)); }
    private sealed class MemoryStore : ICredentialStore
    { public string? Read() => null; public void Save(string token) { } public void Delete() { } public void Dispose() { } }
}
