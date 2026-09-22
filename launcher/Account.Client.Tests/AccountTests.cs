using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;
using Geumeum.Accounts;
using Xunit;

namespace Geumeum.AccountTests;

public sealed class AccountTests
{
    private const string Origin = "https://accounts.example.test";
    private static readonly string Token = new('a', 43);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static AccountProfile Profile => new() { AccountId = "123", LoginId = "tester", Nickname = "Tester", Status = "active" };
    private static HttpResponseMessage Reply(object body) => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(body, Json)) };
    private static HttpResponseMessage Tokens(int seconds = 900, string kind = "launcher") => Reply(new TokenReply
    {
        AccessToken = Token, RefreshToken = new string('b', 43), TokenType = "Bearer",
        ExpiresIn = seconds, SessionExpiresAt = DateTimeOffset.UtcNow.AddDays(1), SessionKind = kind, Account = Profile,
    });

    [Theory]
    [InlineData("http://example.test", true)]
    [InlineData("http://127.0.0.1:8766", false)]
    [InlineData("https://user:password@example.test", true)]
    [InlineData("https://example.test/api", true)]
    [InlineData("https://example.test/?token=secret", true)]
    [InlineData("http://127.0.0.2", true)]
    public void RejectUnsafeOrigins(string origin, bool local)
        => Assert.Throws<AccountException>(() => new AccountApi(origin, local));

    [Fact]
    public void PermitExplicitLocalDevelopment()
    {
        using var api = new AccountApi("http://127.0.0.1:8766", true);
        Assert.True(api.Origin.IsLoopback);
    }

    [Theory]
    [InlineData(401)] [InlineData(429)] [InlineData(302)] [InlineData(500)]
    public async Task ErrorsNeverEchoResponseSecrets(int status)
    {
        using var api = new AccountApi(Origin, handler: new Handler(_ => new((HttpStatusCode)status)
            { Content = new StringContent("password=private-token") }));
        var error = await Assert.ThrowsAsync<AccountException>(() => api.LoginAsync("tester", "secret"));
        Assert.Equal((HttpStatusCode)status, error.Status);
        Assert.DoesNotContain("private-token", error.ToString());
        Assert.DoesNotContain("secret", error.ToString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"access_token\":null}")]
    [InlineData("not-json")]
    public async Task RejectMalformedTokenReply(string body)
    {
        using var api = new AccountApi(Origin, handler: new Handler(_ => new(HttpStatusCode.OK)
            { Content = new StringContent(body) }));
        await Assert.ThrowsAsync<AccountException>(() => api.LoginAsync("tester", "secret"));
    }

    [Fact]
    public async Task BoundResponseSize()
    {
        using var api = new AccountApi(Origin, handler: new Handler(_ => new(HttpStatusCode.OK)
            { Content = new StringContent(new string('x', 65537)) }));
        await Assert.ThrowsAsync<AccountException>(() => api.MeAsync(Token));
    }

    [Fact]
    public async Task ConcurrentRequestsRotateOnlyOnceAndUseBearer()
    {
        int refreshes = 0;
        var store = new MemoryStore();
        using var session = new AccountSession(new AccountApi(Origin, handler: new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("login")) return Tokens(1);
            if (request.RequestUri.AbsolutePath.EndsWith("refresh"))
            {
                Assert.Null(store.Value);
                Interlocked.Increment(ref refreshes);
                return Tokens();
            }
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            return Reply(Profile);
        })), store);
        await session.LoginAsync("tester", "secret", true);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => session.RefreshProfileAsync()));
        Assert.Equal(1, refreshes);
        Assert.NotNull(store.Value);
    }

    [Fact]
    public async Task AmbiguousRefreshDeletesOldCredentialAndDoesNotRetry()
    {
        int calls = 0;
        var store = new MemoryStore { Value = Token };
        using var session = new AccountSession(new AccountApi(Origin, handler: new Handler(_ =>
        {
            calls++;
            Assert.Null(store.Value);
            throw new HttpRequestException("secret");
        })), store);
        await Assert.ThrowsAsync<AccountException>(() => session.RestoreAsync());
        await session.RestoreAsync();
        Assert.Equal(1, calls);
        Assert.Null(session.Profile);
        Assert.Null(store.Value);
    }

    [Fact]
    public async Task OfflineLogoutClearsLocalCredentials()
    {
        var store = new MemoryStore();
        using var session = new AccountSession(new AccountApi(Origin, handler: new Handler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("login") ? Tokens() : throw new HttpRequestException())), store);
        await session.LoginAsync("tester", "secret", true);
        Assert.False(await session.LogoutAsync());
        Assert.Null(session.Profile);
        Assert.Null(store.Value);
    }

    [Fact]
    public async Task UnauthorizedProfileClearsSession()
    {
        var store = new MemoryStore();
        using var session = new AccountSession(new AccountApi(Origin, handler: new Handler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("login") ? Tokens() : new(HttpStatusCode.Unauthorized))), store);
        await session.LoginAsync("tester", "secret", true);
        await Assert.ThrowsAsync<AccountException>(() => session.RefreshProfileAsync());
        Assert.Null(session.Profile);
        Assert.Null(store.Value);
    }

    [Fact]
    public async Task PasswordResponseLossStillClearsSession()
    {
        var store = new MemoryStore();
        using var session = new AccountSession(new AccountApi(Origin, handler: new Handler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("login") ? Tokens() : throw new HttpRequestException())), store);
        await session.LoginAsync("tester", "secret", true);
        await Assert.ThrowsAsync<AccountException>(() => session.ChangePasswordAsync("old", "new"));
        Assert.Null(session.Profile);
        Assert.Null(store.Value);
    }

    [Fact]
    public async Task NonRememberedLoginNeverSavesAndGameReplyIsScoped()
    {
        var store = new MemoryStore();
        using var session = new AccountSession(new AccountApi(Origin, handler: new Handler(request =>
            Tokens(kind: request.RequestUri!.AbsolutePath.EndsWith("game-sessions") ? "game" : "launcher"))), store);
        await session.LoginAsync("tester", "secret", false);
        Assert.Equal("game", (await session.CreateGameSessionAsync()).SessionKind);
        Assert.Null(store.Value);
    }

    [Fact]
    public void WindowsStoreEncryptsIsolatesLocksAndRecoversCorruption()
    {
        string directory = Path.Combine(Path.GetTempPath(), "geumeum-account-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var store = new CredentialStore(directory, new Uri(Origin)))
            {
                store.Save(Token);
                Assert.True(store.Read() == Token);
                string path = Directory.GetFiles(directory, "*.bin").Single();
                Assert.DoesNotContain(Token, Encoding.UTF8.GetString(File.ReadAllBytes(path)));
                Assert.Throws<AccountException>(() => new CredentialStore(directory, new Uri(Origin)));
                using var other = new CredentialStore(directory, new Uri("https://other.example.test"));
                Assert.Null(other.Read());
                File.WriteAllText(path, "corrupt");
                Assert.Throws<AccountException>(() => store.Read());
                Assert.False(File.Exists(path));
                store.Save(Token);
            }
            using var reopened = new CredentialStore(directory, new Uri(Origin));
            Assert.True(reopened.Read() == Token);
            reopened.Delete();
            Assert.Null(reopened.Read());
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(send(request));
    }

    private sealed class MemoryStore : ICredentialStore
    {
        public string? Value;
        public string? Read() => Value;
        public void Save(string token) => Value = token;
        public void Delete() => Value = null;
        public void Dispose() { }
    }
}
