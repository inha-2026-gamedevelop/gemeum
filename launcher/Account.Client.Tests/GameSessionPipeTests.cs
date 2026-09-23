using System.IO;
using System.Text.Json;
using Geumeum.Accounts;
using Geumeum.Network;
using Xunit;

namespace Geumeum.AccountTests;

public sealed class GameSessionPipeTests
{
    private static TokenReply Game() => new()
    {
        SessionKind = "game", AccessToken = new string('a', 43), RefreshToken = new string('b', 43),
        Account = new AccountProfile { AccountId = "123" }, ExpiresIn = 900,
        SessionExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
    };

    [Fact]
    public async Task ReceiveOnlyFromExpectedLauncherAndChild()
    {
        using var pipe = new GameSessionPipe();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task<string> receive = GameSessionPipeClient.ReceiveAsync(pipe.Name, Environment.ProcessId, deadline.Token);
        await pipe.DeliverAsync(Environment.ProcessId, new Uri("https://accounts.example.test"), Game(), deadline.Token);
        string payload = await receive;
        using var json = JsonDocument.Parse(payload);
        Assert.Equal("123", json.RootElement.GetProperty("account_id").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("room_id").ValueKind);
        Assert.Equal(0, json.RootElement.GetProperty("port").GetInt32());
        Assert.False(json.RootElement.GetProperty("is_host").GetBoolean());
    }

    [Fact]
    public async Task UnexpectedChildReceivesNoPayload()
    {
        using var pipe = new GameSessionPipe();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task<string> receive = GameSessionPipeClient.ReceiveAsync(pipe.Name, Environment.ProcessId, deadline.Token);
        await Assert.ThrowsAsync<AccountException>(() => pipe.DeliverAsync(int.MaxValue,
            new Uri("https://accounts.example.test"), Game(), deadline.Token));
        pipe.Dispose();
        await Assert.ThrowsAnyAsync<IOException>(async () => await receive);
    }

    [Fact]
    public async Task UnexpectedLauncherIsRejected()
    {
        using var pipe = new GameSessionPipe();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task<string> receive = GameSessionPipeClient.ReceiveAsync(pipe.Name, int.MaxValue, deadline.Token);
        Task deliver = pipe.DeliverAsync(Environment.ProcessId, new Uri("https://accounts.example.test"), Game(), deadline.Token);
        await Assert.ThrowsAnyAsync<IOException>(async () => await receive);
        await Assert.ThrowsAsync<AccountException>(async () => await deliver);
    }

    [Fact]
    public async Task LauncherTokenCannotBeDelivered()
    {
        using var pipe = new GameSessionPipe();
        TokenReply token = Game();
        token.SessionKind = "launcher";
        await Assert.ThrowsAsync<AccountException>(() => pipe.DeliverAsync(Environment.ProcessId,
            new Uri("https://accounts.example.test"), token));
    }

    [Theory]
    [InlineData("../../other")]
    [InlineData("geumeum-account-wrong")]
    [InlineData("geumeum-account-0000000000000000000000000000000z")]
    public void RejectArbitraryPipeNames(string name) => Assert.False(GameSessionPipeClient.IsValidName(name));
}
