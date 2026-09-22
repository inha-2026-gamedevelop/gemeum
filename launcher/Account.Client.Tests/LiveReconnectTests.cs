using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Geumeum.Accounts;
using Xunit;

namespace Geumeum.AccountTests;

/// <summary>
/// 호스트가 살아 있는 동안 클라이언트 하나가 종료됐다가 같은 계정으로 돌아온다.
/// 실제 Unity 프로세스 3개와 로컬 계정 API를 쓰며, 판정은 호스트 로그와 서버 스냅샷으로 한다.
/// 복귀는 두 번 시험한다 - 종료 직후 곧바로, 그리고 기존 90초 예약이 만료된 뒤.
/// </summary>
public sealed class LiveReconnectTests
{
    private const int HostIndex = 0;
    private const int LeaverIndex = 1;
    private const int StayIndex = 2;
    private const string Resumed = "[RunManager] 런을 재개했다";
    private const string Paused = "[RunManager] 정지 사유를 걸었다: ParticipantMissing";

    private sealed record Peer(Process Process, int Panel, int Artifact);

    /// <summary> 복귀 한 번마다 늘어나야 하는 호스트 로그 횟수. 직전 복귀의 줄을 재사용하지 않는다 </summary>
    private sealed record Evidence(int Accepted, int Restored, int Resumed);

    [LiveDedicatedFact]
    public async Task DepartedMemberReturnsToLiveHostAndKeepsRoleAndRun()
    {
        string executable = Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_DEDICATED")!;
        string origin = Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_ORIGIN")!;
        string artifacts = Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_ARTIFACTS")!;
        var users = JsonSerializer.Deserialize<string[][]>(
            Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_USERS")!)!;
        Assert.True(users.Length >= 4, "Reconnect verification needs a fourth, uninvited account");
        Directory.CreateDirectory(artifacts);

        using var api = new AccountApi(origin, true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        var report = new StringBuilder();
        var parents = new List<TokenReply>();
        var peers = new List<Peer>();
        AccountRoom? room = null;
        string hostLog = Path.Combine(artifacts, "reconnect-host.log");
        try
        {
            foreach (var user in users)
                parents.Add(await api.LoginAsync(user[0], user[1], deadline.Token));
            string leaver = parents[LeaverIndex].Account.AccountId;
            var list = await api.RoomsAsync(parents[0].AccessToken, deadline.Token);
            room = await api.CreateRoomAsync(parents[0].AccessToken, "ReturnVerify", list.BuildId, deadline.Token);
            for (int i = 1; i < 3; i++)
                await api.LobbyAsync(parents[i].AccessToken, room.RoomId, "join", ct: deadline.Token);
            for (int i = 0; i < 3; i++)
            {
                await api.LobbyAsync(parents[i].AccessToken, room.RoomId, "preference", preference: i, ct: deadline.Token);
                await api.LobbyAsync(parents[i].AccessToken, room.RoomId, "ready", enabled: true, ct: deadline.Token);
            }
            room = await api.LobbyAsync(parents[0].AccessToken, room.RoomId, "start", ct: deadline.Token);
            Assert.Equal("listen", room.Hosting);

            for (int i = 0; i < 3; i++)
            {
                peers.Add(await LaunchAsync(api, parents[i], room, executable, origin, artifacts,
                    18140 + i, i, i == HostIndex ? hostLog : Path.Combine(artifacts, $"reconnect-{i}.log"),
                    deadline.Token));
                if (i == HostIndex)
                    room = await WaitForStateAsync(api, parents[0], room, "ready", deadline.Token);
            }
            await WaitForFieldAsync(peers, deadline.Token);
            // 런이 잠긴 것을 호스트가 다음 heartbeat에 알린다. 복귀 경로는 이 상태를 요구한다
            room = await WaitForStateAsync(api, parents[0], room, "running", deadline.Token);
            var before = SlotOf(
                await LiveDedicatedTests.InspectAsync(artifacts, HostIndex, "snapshot", 0, deadline.Token), leaver);
            report.AppendLine($"출발: 3인 Field 진입, 이탈 예정 계정={leaver} weapon={before.GetProperty("Weapon").GetInt32()}");

            var alive = new[] { peers[HostIndex], peers[StayIndex] };
            int hostPid = peers[HostIndex].Process.Id;
            int stayPid = peers[StayIndex].Process.Id;
            int leftPid = peers[LeaverIndex].Process.Id;

            // 1) 종료하자마자 곧바로 다시 띄운다. 사용자가 실제로 하는 행동이고, 한 번에 붙어야 한다
            var baseline = Count(hostLog, leaver);
            var clock = Stopwatch.StartNew();
            peers[LeaverIndex].Process.Kill();
            await peers[LeaverIndex].Process.WaitForExitAsync(deadline.Token);
            var returned = await ReturnAsync(api, parents[LeaverIndex], room, executable, origin, artifacts,
                18143, 3, Path.Combine(artifacts, "reconnect-return-1.log"), hostLog, leaver, alive,
                baseline, deadline.Token);
            peers.Add(returned);
            clock.Stop();
            Assert.NotEqual(leftPid, returned.Process.Id);
            await AssertRestoredAsync(api, parents[0], room, artifacts, leaver, before, report, deadline.Token);
            report.AppendLine($"복귀1(즉시): 종료 후 {clock.Elapsed.TotalSeconds:F1}초 만에 한 번에 재접속, pid={returned.Process.Id}");
            Assert.False(peers[HostIndex].Process.HasExited);
            Assert.False(peers[StayIndex].Process.HasExited);
            Assert.Equal(hostPid, peers[HostIndex].Process.Id);
            Assert.Equal(stayPid, peers[StayIndex].Process.Id);

            // 2) 파티에 없던 계정은 런 중에 어떤 경로로도 들어올 수 없다
            await RejectOutsiderAsync(api, parents[3], room, deadline.Token);
            report.AppendLine("미참가 계정: lobby/relay/tickets 모두 거부 확인");

            // 3) 기존 90초 예약 만료를 넘긴 복귀. 이 구간이 수정 전에 영구 차단되던 지점이다
            leftPid = returned.Process.Id;
            int pausedBefore = Occurrences(ReadLog(hostLog), Paused);
            returned.Process.Kill();
            await returned.Process.WaitForExitAsync(deadline.Token);
            await WaitForLogAsync(hostLog, Paused, alive, deadline.Token, pausedBefore + 1);
            await Task.Delay(TimeSpan.FromSeconds(100), deadline.Token);
            var held = (await api.RoomsAsync(parents[0].AccessToken, deadline.Token)).Rooms
                .Single(r => r.RoomId == room!.RoomId);
            Assert.Contains(held.Members, m => m.AccountId == leaver);
            Assert.DoesNotContain(held.Members, m => m.AccountId == leaver && m.Connected);
            report.AppendLine("100초 무접속 후에도 출발 멤버의 자리가 방 명단에 남아 있다");

            baseline = Count(hostLog, leaver);
            var late = await ReturnAsync(api, parents[LeaverIndex], room, executable, origin, artifacts,
                18144, 4, Path.Combine(artifacts, "reconnect-return-2.log"), hostLog, leaver, alive,
                baseline, deadline.Token);
            peers.Add(late);
            Assert.NotEqual(leftPid, late.Process.Id);
            await AssertRestoredAsync(api, parents[0], room, artifacts, leaver, before, report, deadline.Token);
            report.AppendLine($"복귀2(90초 예약 만료 이후): 동일 계정 복귀 성공 pid={late.Process.Id}");

            Assert.False(peers[HostIndex].Process.HasExited);
            Assert.False(peers[StayIndex].Process.HasExited);
            Assert.Equal(hostPid, peers[HostIndex].Process.Id);
            Assert.Equal(stayPid, peers[StayIndex].Process.Id);
            report.Insert(0, "PASS: 즉시 복귀와 만료 후 복귀 모두 한 번에 성공, 역할/런 복원, 외부인 거부\n");
            await File.WriteAllTextAsync(
                Path.Combine(artifacts, "reconnect-result.txt"), report.ToString(), deadline.Token);
        }
        finally
        {
            foreach (var peer in peers) Terminate(peer);
            if (room != null)
            {
                try { await api.CloseRoomAsync(parents[0].AccessToken, room.RoomId); }
                catch (AccountException) { }
            }
            foreach (var parent in parents)
            {
                try { await api.LogoutAsync(parent.AccessToken); }
                catch (AccountException) { }
            }
        }
    }

    /// <summary> 같은 부모 계정 세션으로 새 game session과 pipe를 만들어 게임을 다시 띄운다 </summary>
    private static async Task<Peer> ReturnAsync(AccountApi api, TokenReply parent, AccountRoom? room,
        string executable, string origin, string artifacts, int panel, int artifact, string log,
        string hostLog, string leaver, IReadOnlyList<Peer> alive, Evidence baseline, CancellationToken ct)
    {
        var current = (await api.RoomsAsync(parent.AccessToken, ct)).Rooms.Single(r => r.RoomId == room!.RoomId);
        Assert.Equal("running", current.State);
        Assert.Contains(current.Members, m => m.AccountId == leaver);
        var peer = await LaunchAsync(api, parent, current, executable, origin, artifacts, panel, artifact, log, ct);
        try
        {
            // 세 줄 모두 이번 복귀에서 새로 찍혀야 한다. 직전 복귀의 줄로는 통과할 수 없다
            var watching = alive.Append(peer).ToList();
            await WaitForLogAsync(hostLog, Accepted(leaver), watching, ct, baseline.Accepted + 1);
            await WaitForLogAsync(hostLog, Restored(leaver), watching, ct, baseline.Restored + 1);
            await WaitForLogAsync(hostLog, Resumed, watching, ct, baseline.Resumed + 1);
            await WaitForFieldAsync(new[] { peer }, ct);
            return peer;
        }
        catch
        {
            Terminate(peer);
            throw;
        }
    }

    private static async Task<Peer> LaunchAsync(AccountApi api, TokenReply parent, AccountRoom room,
        string executable, string origin, string artifacts, int panel, int artifact, string log,
        CancellationToken ct)
    {
        using var pipe = new GameSessionPipe();
        var game = await api.CreateGameSessionAsync(parent.AccessToken, ct);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(executable),
        };
        foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("GEUMEUM_")).ToArray())
            start.Environment.Remove(key);
        foreach (string arg in new[] { "-batchmode", "-nographics", "-account-pipe", pipe.Name,
            "-verification-command-path", Path.Combine(artifacts, $"lobby-{artifact}.json"),
            "-launcher-pid", Environment.ProcessId.ToString(), "-devpanelport", panel.ToString(),
            "-logFile", log }) start.ArgumentList.Add(arg);
        var peer = new Peer(Process.Start(start)!, panel, artifact);
        try
        {
            // 세션 전달이 실패해도 방금 띄운 프로세스를 남기지 않는다
            await pipe.DeliverAsync(peer.Process.Id, new Uri(origin), game, ct, room);
            return peer;
        }
        catch
        {
            Terminate(peer);
            throw;
        }
    }

    /// <summary> 파티 명단에 없는 계정은 로비, 릴레이, 티켓 어디로도 진행 중인 방에 들어올 수 없다 </summary>
    private static async Task RejectOutsiderAsync(AccountApi api, TokenReply outsider, AccountRoom? room,
        CancellationToken ct)
    {
        await Assert.ThrowsAsync<AccountException>(
            () => api.LobbyAsync(outsider.AccessToken, room!.RoomId, "join", ct: ct));
        var game = await api.CreateGameSessionAsync(outsider.AccessToken, ct);
        using var http = new HttpClient { BaseAddress = api.Origin, Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", game.AccessToken);
        var attempts = new (string Path, object Body)[]
        {
            ($"v1/rooms/{room!.RoomId}/relay", new { }),
            ($"v1/rooms/{room.RoomId}/tickets", new { challenge = new string('b', 64), build_id = room.BuildId }),
        };
        foreach (var attempt in attempts)
        {
            using var content = new StringContent(
                JsonSerializer.Serialize(attempt.Body), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(attempt.Path, content, ct);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        await api.LogoutAsync(game.AccessToken, ct);
    }

    /// <summary> 돌아온 사람이 자리, 연결, 런 참가자 모두에서 되살아났는지 본다 </summary>
    private static async Task AssertRestoredAsync(AccountApi api, TokenReply parent, AccountRoom? room,
        string artifacts, string leaver, JsonElement before, StringBuilder report, CancellationToken ct)
    {
        var snapshot = await LiveDedicatedTests.InspectAsync(artifacts, HostIndex, "snapshot", 0, ct);
        Assert.Equal(3, snapshot.GetProperty("lobbySlots").GetArrayLength());
        Assert.Contains(snapshot.GetProperty("players").EnumerateArray(),
            player => player.GetProperty("participant").GetString() == $"Human:{leaver}");
        AssertSameSeat(before, SlotOf(snapshot, leaver), report);
        var current = (await api.RoomsAsync(parent.AccessToken, ct)).Rooms.Single(r => r.RoomId == room!.RoomId);
        Assert.Equal(3, current.Members.Count);
        Assert.Contains(current.Members, m => m.AccountId == leaver && m.Connected);
    }

    /// <summary>
    /// 배분된 역할은 어떤 빌드에서도 그대로 돌아와야 한다. 로비 희망(HasPreference)은
    /// LobbyRoster 수정을 담은 빌드에서만 복원되므로, 그 빌드로 검증할 때 플래그로 켠다
    /// </summary>
    private static void AssertSameSeat(JsonElement before, JsonElement after, StringBuilder report)
    {
        foreach (string field in new[] { "HasWeapon", "HasCharacter", "IsRoomOwner" })
            Assert.Equal(before.GetProperty(field).GetBoolean(), after.GetProperty(field).GetBoolean());
        foreach (string field in new[] { "Weapon", "Character" })
            Assert.Equal(before.GetProperty(field).GetInt32(), after.GetProperty(field).GetInt32());
        bool kept = before.GetProperty("HasPreference").GetBoolean() == after.GetProperty("HasPreference").GetBoolean()
            && before.GetProperty("PreferredCharacter").GetInt32() == after.GetProperty("PreferredCharacter").GetInt32();
        report.AppendLine($"로비 희망 복원={kept} (희망={after.GetProperty("PreferredCharacter").GetInt32()})");
        if (Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_PREFERENCE") == "1")
            Assert.True(kept, "Rebuilt client must also return the launcher weapon preference");
    }

    private static string Accepted(string leaver) => $"두고 간 자리가 있어 받는다: platformId={leaver}";

    private static string Restored(string leaver) => $"platformId={leaver}에 이탈 직전 자리와 역할을 돌려줬다";

    private static Evidence Count(string hostLog, string leaver)
    {
        string text = ReadLog(hostLog);
        return new Evidence(
            Occurrences(text, Accepted(leaver)), Occurrences(text, Restored(leaver)), Occurrences(text, Resumed));
    }

    private static JsonElement SlotOf(JsonElement snapshot, string accountId)
        => snapshot.GetProperty("lobbySlots").EnumerateArray()
            .Single(s => s.GetProperty("PlatformId").GetUInt64() == ulong.Parse(accountId));

    private static async Task<AccountRoom> WaitForStateAsync(AccountApi api, TokenReply parent,
        AccountRoom room, string state, CancellationToken ct)
    {
        while (true)
        {
            var current = (await api.RoomsAsync(parent.AccessToken, ct)).Rooms
                .SingleOrDefault(r => r.RoomId == room.RoomId);
            Assert.NotNull(current);
            if (current!.State == state) return current;
            await Task.Delay(500, ct);
        }
    }

    private static async Task WaitForFieldAsync(IEnumerable<Peer> peers, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        foreach (var peer in peers)
        {
            while (true)
            {
                Assert.False(peer.Process.HasExited, "Client exited before reaching Field; inspect the logs");
                try
                {
                    string state = await http.GetStringAsync($"http://127.0.0.1:{peer.Panel}/api/state", ct);
                    if (state.Contains("Farming")) break;
                }
                catch (HttpRequestException) { }
                await Task.Delay(500, ct);
            }
        }
    }

    /// <summary> 살아 있어야 하는 프로세스만 넘긴다. 일부러 죽인 클라이언트는 포함하지 않는다 </summary>
    private static async Task WaitForLogAsync(string path, string needle, IEnumerable<Peer> alive,
        CancellationToken ct, int count = 1)
    {
        while (Occurrences(ReadLog(path), needle) < count)
        {
            foreach (var peer in alive)
                Assert.False(peer.Process.HasExited,
                    $"Peer on panel {peer.Panel} exited ({Describe(peer)}) while waiting for: {needle}");
            await Task.Delay(300, ct);
        }
    }

    /// <summary> 종료 코드를 남긴다. 12는 클라이언트가 연결을 잃고 스스로 닫은 것이다 </summary>
    private static string Describe(Peer peer)
    {
        try { return $"exit {peer.Process.ExitCode}"; }
        catch (InvalidOperationException) { return "exit unknown"; }
    }

    private static void Terminate(Peer peer)
    {
        try
        {
            if (!peer.Process.HasExited)
            {
                peer.Process.Kill();
                peer.Process.WaitForExit(10000);
            }
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        finally { peer.Process.Dispose(); }
    }

    private static int Occurrences(string text, string needle) => text.Split(needle).Length - 1;

    private static string ReadLog(string path)
    {
        if (!File.Exists(path)) return "";
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
