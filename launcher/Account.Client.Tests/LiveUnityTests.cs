using System.Diagnostics;
using System.IO;
using Geumeum.Accounts;
using Xunit;

namespace Geumeum.AccountTests;

public sealed class LiveUnityFactAttribute : FactAttribute
{
    public LiveUnityFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_UNITY") == null)
            Skip = "Build AccountVerification and run backend/scripts/smoke_local.py --unity.";
    }
}

public sealed class LiveUnityTests
{
    [LiveUnityFact]
    public async Task RevokedSessionCannotPassClientAuthentication()
    {
        string executable = Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_UNITY")!;
        string origin = Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_ORIGIN")!;
        string artifacts = Path.Combine(Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_ARTIFACTS")!, "unity-denied-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifacts);
        using var api = new AccountApi(origin, true);
        TokenReply parent = await api.LoginAsync(Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_LOGIN")!,
            Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_PASSWORD")!);
        TokenReply game = await api.CreateGameSessionAsync(parent.AccessToken);
        await api.LogoutAsync(parent.AccessToken);
        using var pipe = new GameSessionPipe();
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false,
            CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("GEUMEUM_SMOKE_")).ToArray()) start.Environment.Remove(key);
        foreach (string arg in new[] { "-batchmode", "-nographics", "-account-pipe", pipe.Name,
            "-launcher-pid", Environment.ProcessId.ToString(), "-host", "-platformid", "7201",
            "-minplayers", "3", "-port", "17973", "-devpanelport", "17974", "-logFile", Path.Combine(artifacts, "game-0.log") })
            start.ArgumentList.Add(arg);
        using Process child = Process.Start(start)!;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await pipe.DeliverAsync(child.Id, new Uri(origin), game, timeout.Token);
            await child.WaitForExitAsync(timeout.Token);
            Assert.Equal(12, child.ExitCode);
            string log = ReadLog(artifacts, 0);
            Assert.False(log.Contains("[GeumeumAuthenticator] 승인:"), "Revoked account reached client authentication");
            Assert.False(log.Contains(game.AccessToken) || log.Contains(game.RefreshToken), "Credential found in game log");
        }
        finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } }
    }

    [LiveUnityFact]
    public async Task ThreeGameProcessesReceiveVerifyRefreshAndLoseRevokedSessions()
    {
        string executable = Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_UNITY")!;
        string origin = Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_ORIGIN")!;
        string artifacts = Path.Combine(Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_ARTIFACTS")!, "unity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifacts);
        using var api = new AccountApi(origin, true);
        TokenReply parent = await api.LoginAsync(Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_LOGIN")!,
            Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_PASSWORD")!);
        var children = new List<Process>();
        var secrets = new List<string> { parent.AccessToken, parent.RefreshToken };
        try
        {
            for (int i = 0; i < 3; i++)
            {
                using var pipe = new GameSessionPipe();
                TokenReply game = await api.CreateGameSessionAsync(parent.AccessToken);
                secrets.Add(game.AccessToken); secrets.Add(game.RefreshToken);
                if (i == 0) game.ExpiresIn = 1; // force the real runtime refresh on its next 30-second poll
                var start = new ProcessStartInfo(executable)
                {
                    WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false,
                    CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                };
                foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("GEUMEUM_SMOKE_")).ToArray())
                    start.Environment.Remove(key); // game must obtain its own credentials only through the pipe
                string[] args = { "-batchmode", "-nographics", "-account-pipe", pipe.Name,
                    "-launcher-pid", Environment.ProcessId.ToString(), "-platformid", (7101 + i).ToString(),
                    "-minplayers", "3", "-port", "17993", "-devpanelport", (17994 + i).ToString(),
                    "-logFile", Path.Combine(artifacts, $"game-{i}.log") };
                foreach (string arg in args) start.ArgumentList.Add(arg);
                if (i == 0) start.ArgumentList.Add("-host");
                else { start.ArgumentList.Add("-client"); start.ArgumentList.Add("127.0.0.1"); }
                Process process = Process.Start(start)!;
                children.Add(process);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                await pipe.DeliverAsync(process.Id, new Uri(origin), game, deadline.Token);
                await WaitForAsync(() => ReadLog(artifacts, i).Contains("[LauncherGameSession] 계정 세션 확인:"), 60);
                Assert.False(process.HasExited, "Game exited during account verification");
            }
            await WaitForAsync(() => ReadLog(artifacts, 0).Contains("[LauncherGameSession] 게임 세션 갱신 확인"), 50);
            await WaitForAsync(() => ReadLog(artifacts, 0).Split("[GeumeumAuthenticator] 승인:").Length >= 4, 60);
            await api.LogoutAsync(parent.AccessToken);
            await WaitForAsync(() => children.All(p => p.HasExited), 50);
            Assert.All(children, p => Assert.Equal(12, p.ExitCode));
            foreach (string secret in secrets)
                Assert.False(Directory.GetFiles(artifacts, "*.log").Any(path => File.ReadAllText(path).Contains(secret)),
                    "Credential found in a game log");
            File.WriteAllText(Path.Combine(artifacts, "result.txt"), "PASS: 3 game sessions, KCP admission, refresh, parent revocation, exit 12, no credential log\n");
        }
        finally
        {
            foreach (Process child in children)
            {
                if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); }
                child.Dispose();
            }
            try { await api.LogoutAsync(parent.AccessToken); } catch (AccountException) { }
        }
    }

    private static string ReadLog(string directory, int i)
    {
        string path = Path.Combine(directory, $"game-{i}.log");
        if (!File.Exists(path)) return "";
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static async Task WaitForAsync(Func<bool> condition, int seconds)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Unity integration condition timed out; inspect local game logs.");
            await Task.Delay(250);
        }
    }
}
