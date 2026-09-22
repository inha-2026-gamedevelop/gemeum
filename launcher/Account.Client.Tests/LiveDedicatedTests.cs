using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Geumeum.Accounts;
using Xunit;

namespace Geumeum.AccountTests;

public sealed class LiveDedicatedFactAttribute : FactAttribute
{
    public LiveDedicatedFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_DEDICATED") == null)
            Skip = "Run backend/scripts/smoke_dedicated.py with the local API and worker.";
    }
}

public sealed class LiveDedicatedTests
{
    [LiveDedicatedFact]
    public async Task OwnerFillsRoomWithTwoBots()
    {
        string executable = Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_DEDICATED")!;
        string origin = Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_ORIGIN")!;
        string artifacts = Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_ARTIFACTS")!;
        var users = JsonSerializer.Deserialize<string[][]>(Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_USERS")!)!;
        using var api = new AccountApi(origin, true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var parent = await api.LoginAsync(users[0][0], users[0][1], timeout.Token);
        AccountRoom? room = null;
        Process? child = null;
        try
        {
            var list = await api.RoomsAsync(parent.AccessToken, timeout.Token);
            room = await api.CreateRoomAsync(parent.AccessToken, "BotVerify", list.BuildId, timeout.Token);
            Assert.Equal("waiting", room.State);
            await api.LobbyAsync(parent.AccessToken, room.RoomId, "bots", enabled: true, ct: timeout.Token);
            await api.LobbyAsync(parent.AccessToken, room.RoomId, "preference", preference: 2, ct: timeout.Token);
            await api.LobbyAsync(parent.AccessToken, room.RoomId, "ready", enabled: true, ct: timeout.Token);
            room = await api.LobbyAsync(parent.AccessToken, room.RoomId, "start", ct: timeout.Token);
            while (room.State != "ready" && room.Hosting != "listen")
            {
                await Task.Delay(500, timeout.Token);
                room = (await api.RoomsAsync(parent.AccessToken, timeout.Token)).Rooms.Single(r => r.RoomId == room.RoomId);
            }
            using var pipe = new GameSessionPipe();
            var game = await api.CreateGameSessionAsync(parent.AccessToken, timeout.Token);
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable) };
            foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("GEUMEUM_")).ToArray()) start.Environment.Remove(key);
            foreach (string arg in new[] { "-screen-width", "1280", "-screen-height", "720", "-screen-fullscreen", "0",
                "-account-pipe", pipe.Name, "-launcher-pid", Environment.ProcessId.ToString(),
                "-verification-command-path", Path.Combine(artifacts, "lobby-4.json"), "-devpanelport", "18121",
                "-logFile", Path.Combine(artifacts, "bot-owner.log") }) start.ArgumentList.Add(arg);
            child = Process.Start(start)!;
            await pipe.DeliverAsync(child.Id, new Uri(origin), game, timeout.Token, room);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            while (true)
            {
                await Task.Delay(500, timeout.Token);
                var initial = await InspectAsync(artifacts, 4, "snapshot", 0, timeout.Token);
                if (initial.GetProperty("lobbySlots").GetArrayLength() == 1) break;
            }
            await Task.Delay(1000, timeout.Token);
            var lobby = await InspectAsync(artifacts, 4, "capture", 0, timeout.Token);
            Assert.Single(lobby.GetProperty("lobbySlots").EnumerateArray());
            Assert.True(lobby.GetProperty("lobbySlots")[0].GetProperty("IsRoomOwner").GetBoolean());
            JsonElement state;
            do
            {
                await Task.Delay(500, timeout.Token);
                state = await InspectAsync(artifacts, 4, "snapshot", 0, timeout.Token);
            } while (state.GetProperty("players").EnumerateArray().Count(p => p.GetProperty("participant").GetString()!.StartsWith("Bot:")) != 2);
            while (!(await http.GetStringAsync("http://127.0.0.1:18121/api/state", timeout.Token)).Contains("Farming"))
                await Task.Delay(500, timeout.Token);
            await Task.Delay(2000, timeout.Token);
            state = await InspectAsync(artifacts, 4, "capture", 0, timeout.Token);
            Assert.Equal(4, state.GetProperty("runScene").GetInt32());
            Assert.Equal(0, state.GetProperty("transitionState").GetInt32());
            if (Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_VISUAL_HOLD") == "1")
                await Task.Delay(20000, timeout.Token);
            Assert.True(state.GetProperty("lobbyBotFill").GetBoolean());
            Assert.Equal(2, state.GetProperty("lobbySlots")[0].GetProperty("PreferredCharacter").GetInt32());
            File.WriteAllText(Path.Combine(artifacts, "bot-result.txt"), "PASS: launcher prepared weapon, bots and readiness; game entered Field with one human + two bots without game lobby input");
            await api.CloseRoomAsync(parent.AccessToken, room.RoomId, timeout.Token);
            using var disconnectDeadline = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            disconnectDeadline.CancelAfter(TimeSpan.FromSeconds(40));
            await child.WaitForExitAsync(disconnectDeadline.Token);
            Assert.Equal(room.Hosting == "listen" ? 13 : 12, child.ExitCode);
        }
        finally
        {
            if (child != null) { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } child.Dispose(); }
            if (room != null) await api.CloseRoomAsync(parent.AccessToken, room.RoomId);
            await api.LogoutAsync(parent.AccessToken);
        }
    }

    [LiveDedicatedFact]
    public async Task ThreeAccountsJoinDedicatedRoomAndRevocationDisconnects()
    {
        string executable = Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_DEDICATED")!;
        string origin = Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_ORIGIN")!;
        string artifacts = Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_ARTIFACTS")!;
        var users = JsonSerializer.Deserialize<string[][]>(Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_USERS")!)!;
        Directory.CreateDirectory(artifacts);
        using var api = new AccountApi(origin, true);
        var parents = new List<TokenReply>();
        var children = new List<Process>();
        AccountRoom? room = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        try
        {
            foreach (var user in users) parents.Add(await api.LoginAsync(user[0], user[1], deadline.Token));
            var list = await api.RoomsAsync(parents[0].AccessToken, deadline.Token);
            room = await api.CreateRoomAsync(parents[0].AccessToken, "LocalVerify", list.BuildId, deadline.Token);
            Assert.Equal("waiting", room.State);
            for (int i = 1; i < 3; i++) await api.LobbyAsync(parents[i].AccessToken, room.RoomId, "join", ct: deadline.Token);
            await Assert.ThrowsAsync<AccountException>(() => api.LobbyAsync(parents[1].AccessToken, room.RoomId, "bots", enabled: true, ct: deadline.Token));
            for (int i = 0; i < 3; i++)
            {
                await api.LobbyAsync(parents[i].AccessToken, room.RoomId, "preference", preference: i, ct: deadline.Token);
                await api.LobbyAsync(parents[i].AccessToken, room.RoomId, "ready", enabled: true, ct: deadline.Token);
            }
            room = await api.LobbyAsync(parents[0].AccessToken, room.RoomId, "start", ct: deadline.Token);
            while (room.State != "ready" && room.Hosting != "listen")
            {
                await Task.Delay(500, deadline.Token);
                list = await api.RoomsAsync(parents[0].AccessToken, deadline.Token);
                room = list.Rooms.Single(r => r.RoomId == room.RoomId);
            }
            for (int i = 0; i < 3; i++)
            {
                using var pipe = new GameSessionPipe();
                var game = await api.CreateGameSessionAsync(parents[i].AccessToken, deadline.Token);
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(executable) };
                foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("GEUMEUM_")).ToArray()) start.Environment.Remove(key);
                foreach (string arg in new[] { "-batchmode", "-nographics", "-account-pipe", pipe.Name,
                    "-verification-command-path", Path.Combine(artifacts, $"lobby-{i}.json"),
                    "-launcher-pid", Environment.ProcessId.ToString(), "-devpanelport", (18101+i).ToString(),
                    "-logFile", Path.Combine(artifacts, $"client-{i}.log") }) start.ArgumentList.Add(arg);
                var child = Process.Start(start)!;
                children.Add(child);
                await pipe.DeliverAsync(child.Id, new Uri(origin), game, deadline.Token, room);
                if (i == 0 && room.Hosting == "listen")
                {
                    do
                    {
                        await Task.Delay(500, deadline.Token);
                        room = (await api.RoomsAsync(parents[0].AccessToken, deadline.Token)).Rooms.Single(r => r.RoomId == room.RoomId);
                    } while (room.State != "ready");
                }
                if (i == 1)
                {
                    await Task.Delay(2000, deadline.Token);
                    var waiting = await InspectAsync(artifacts, 0, "snapshot", 0, deadline.Token);
                    Assert.NotEqual(3, waiting.GetProperty("lobbySlots").GetArrayLength());
                    Assert.All(waiting.GetProperty("lobbySlots").EnumerateArray(), s => Assert.False(s.GetProperty("IsReady").GetBoolean()));
                }
            }
            string serverLog = room.Hosting == "listen" ? Path.Combine(artifacts, "client-0.log") : Path.Combine(Path.GetDirectoryName(artifacts)!, "game-servers", room.RoomId + ".log");
            while (ReadLog(serverLog).Split("source=Account").Length < 4)
            {
                Assert.All(children, p => Assert.False(p.HasExited, "Account client exited; inspect logs"));
                await Task.Delay(500, deadline.Token);
            }
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            // Owner identity must be the API creator, visible consistently on all three peers
            for (int i = 0; i < 3; i++)
            {
                JsonElement snapshot;
                do { snapshot = await InspectAsync(artifacts, i, "snapshot", 0, deadline.Token); }
                while (snapshot.GetProperty("lobbySlots").GetArrayLength() != 3);
                var owners = snapshot.GetProperty("lobbySlots").EnumerateArray()
                    .Where(s => s.GetProperty("IsRoomOwner").GetBoolean()).ToArray();
                Assert.Single(owners);
                Assert.Equal(ulong.Parse(parents[0].Account.AccountId), owners[0].GetProperty("PlatformId").GetUInt64());
            }
            var populated = (await api.RoomsAsync(parents[0].AccessToken, deadline.Token)).Rooms.Single(r => r.RoomId == room.RoomId);
            Assert.Equal(3, populated.Members.Count);
            Assert.Single(populated.Members, m => m.IsOwner);
            Assert.All(populated.Members, m => Assert.True(m.Connected));
            for (int i = 0; i < 3; i++)
            {
                string state;
                do
                {
                    await Task.Delay(500, deadline.Token);
                    state = await http.GetStringAsync($"http://127.0.0.1:{18101+i}/api/state", deadline.Token);
                } while (!state.Contains("Farming"));
                File.WriteAllText(Path.Combine(artifacts, $"field-{i}.json"), state);
                Assert.Contains("Farming", state);
                var finalLobby = await InspectAsync(artifacts, i, "snapshot", 0, deadline.Token);
                foreach (var slot in finalLobby.GetProperty("lobbySlots").EnumerateArray())
                {
                    int expected = parents.FindIndex(p => ulong.Parse(p.Account.AccountId) == slot.GetProperty("PlatformId").GetUInt64());
                    Assert.Equal(expected, slot.GetProperty("PreferredCharacter").GetInt32());
                }
            }
            await api.LogoutAsync(parents[2].AccessToken, deadline.Token);
            using (var serverDeadline = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token))
            {
                serverDeadline.CancelAfter(TimeSpan.FromSeconds(15));
                while (!ReadLog(serverLog).Contains("해제: " + parents[2].Account.Nickname + "("))
                    await Task.Delay(100, serverDeadline.Token);
                Assert.False(children[2].HasExited, "Expected server enforcement before the client session poll exits");
            }
            await children[2].WaitForExitAsync(deadline.Token);
            Assert.Equal(12, children[2].ExitCode);
            Assert.False(children[0].HasExited);
            Assert.False(children[1].HasExited);
            if (room.Hosting == "listen")
            {
                children[0].Kill();
                await children[0].WaitForExitAsync(deadline.Token);
                await children[1].WaitForExitAsync(deadline.Token);
                Assert.Equal(12, children[1].ExitCode);
                while ((await api.RoomsAsync(parents[1].AccessToken, deadline.Token)).Rooms.Any(r => r.RoomId == room.RoomId))
                    await Task.Delay(500, deadline.Token);
            }
            File.WriteAllText(Path.Combine(artifacts, "result.txt"), "PASS: three accounts reached Field; revoked client exited; listen host loss disconnects peer and expires room");
        }
        finally
        {
            foreach (var child in children) { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } child.Dispose(); }
            if (room != null) await api.CloseRoomAsync(parents[0].AccessToken, room.RoomId);
            foreach (var parent in parents) { try { await api.LogoutAsync(parent.AccessToken); } catch (AccountException) { } }
        }
    }

    internal static async Task<JsonElement> InspectAsync(string artifacts, int index, string verb, int value, CancellationToken ct)
    {
        string id = Guid.NewGuid().ToString("N");
        string path = Path.Combine(artifacts, $"lobby-{index}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new {
            executionId = id, commands = new[] { new { commandId = 1, verb, index = value } }
        }), ct);
        string responsePath = Path.ChangeExtension(path, ".response.json");
        while (true)
        {
            await Task.Delay(300, ct);
            try
            {
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(responsePath, ct));
                if (document.RootElement.GetProperty("executionId").GetString() == id)
                    return document.RootElement.GetProperty("snapshot").Clone();
            }
            catch (IOException) { }
            catch (JsonException) { }
        }
    }

    private static string ReadLog(string path)
    {
        if (!File.Exists(path)) return "";
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
