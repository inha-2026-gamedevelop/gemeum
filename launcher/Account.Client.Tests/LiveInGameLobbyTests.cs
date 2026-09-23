using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Geumeum.Accounts;
using Xunit;

namespace Geumeum.AccountTests;

public sealed class LiveInGameLobbyFactAttribute : FactAttribute
{
    public LiveInGameLobbyFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_DEDICATED") == null)
            Skip = "Run backend/scripts/smoke_ingame_lobby.py against the local API and relay.";
    }
}

public sealed class LiveInGameLobbyTests
{
    [LiveInGameLobbyFact]
    public async Task ThreeClientsPrepareDepartReturnAndPrepareAgain()
    {
        string executable = Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_DEDICATED")!;
        string origin = Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_ORIGIN")!;
        string artifacts = Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_ARTIFACTS")!;
        string[][] users = JsonSerializer.Deserialize<string[][]>(
            Environment.GetEnvironmentVariable("GEUMEUM_SMOKE_USERS")!)!;
        Directory.CreateDirectory(artifacts);
        using var api = new AccountApi(origin, true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var parents = new List<TokenReply>();
        var games = new List<TokenReply>();
        var children = new List<Process>();
        var createdRooms = new List<string>();
        try
        {
            foreach (string[] user in users)
                parents.Add(await api.LoginAsync(user[0], user[1], deadline.Token));
            for (int index = 0; index < 3; index++)
            {
                using var pipe = new GameSessionPipe();
                TokenReply game = await api.CreateGameSessionAsync(parents[index].AccessToken, deadline.Token);
                games.Add(game);
                var start = new ProcessStartInfo(executable)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(executable),
                };
                foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("GEUMEUM_")).ToArray())
                    start.Environment.Remove(key);
                foreach (string arg in new[]
                {
                    "-screen-fullscreen", "0", "-screen-width", index == 0 ? "1280" : "960",
                    "-screen-height", index == 0 ? "720" : "540",
                    "-nocursorlock", "-account-pipe", pipe.Name,
                    "-launcher-pid", Environment.ProcessId.ToString(),
                    "-verification-command-path", CommandPath(artifacts, index),
                    "-devpanelport", (18801 + index).ToString(),
                    "-logFile", Path.Combine(artifacts, $"game-{index}.log"),
                }) start.ArgumentList.Add(arg);
                Process child = Process.Start(start)!;
                children.Add(child);
                await pipe.DeliverAsync(child.Id, new Uri(origin), game, deadline.Token);
            }

            // Every UI step uses front_* verbs: real EventSystem raycast and pointer click, no direct panel calls.
            for (int index = 0; index < 3; index++)
            {
                await WaitSnapshotAsync(artifacts, children, index, snapshot =>
                    Flag(snapshot, "frontendSessionReady")
                    && Value(snapshot, "frontendActiveScene") == "Title"
                    && Value(snapshot, "frontendTitleState") == "prompt"
                    && !string.IsNullOrEmpty(Value(snapshot, "frontendEventSystem"))
                    && !Flag(snapshot, "frontendVisible"),
                    TimeSpan.FromSeconds(75), deadline.Token);
                if (index == 0)
                    await CaptureAsync(artifacts, children, index, 101, 1280, 720, deadline.Token);
                await ClickAsync(artifacts, children, index, "title.prompt", deadline.Token);
                await WaitSnapshotAsync(artifacts, children, index, snapshot =>
                    Value(snapshot, "frontendTitleState") == "menu", TimeSpan.FromSeconds(10), deadline.Token);
                if (index == 0)
                    await CaptureAsync(artifacts, children, index, 102, 1280, 720, deadline.Token);
                await ClickAsync(artifacts, children, index, "title.find", deadline.Token);
                await WaitSnapshotAsync(artifacts, children, index, snapshot =>
                    Value(snapshot, "frontendActiveScene") == "Lobby"
                    && Value(snapshot, "frontendScreen") == "rooms" && !Flag(snapshot, "frontendBusy")
                    && Flag(snapshot, "frontendVisible") && Flag(snapshot, "frontendOwnInput"),
                    TimeSpan.FromSeconds(35), deadline.Token);
                // An empty directory is valid; create and refresh must still be reachable by the pointer.
                await ProbeAsync(artifacts, children, index, "rooms.create", deadline.Token);
                await ProbeAsync(artifacts, children, index, "rooms.refresh", deadline.Token);
                if (index == 0)
                    await CaptureAsync(artifacts, children, index, 103, 1280, 720, deadline.Token);
            }

            // An invalid name stays on the rooms page with an error and sends no request.
            await TypeAsync(artifacts, children, 0, "rooms.input", "A", deadline.Token);
            await ClickAsync(artifacts, children, 0, "rooms.create", deadline.Token);
            await WaitSnapshotAsync(artifacts, children, 0, snapshot =>
                Value(snapshot, "frontendScreen") == "rooms"
                && (Value(snapshot, "frontendStatus") ?? "").Contains("2~16")
                && string.IsNullOrEmpty(Value(snapshot, "frontendCurrentRoomId")),
                TimeSpan.FromSeconds(10), deadline.Token);

            // The owner navigates back to Title and uses its create flow.
            await ClickAsync(artifacts, children, 0, "rooms.back", deadline.Token);
            await WaitSnapshotAsync(artifacts, children, 0, snapshot =>
                Value(snapshot, "frontendActiveScene") == "Title" && Value(snapshot, "frontendTitleState") == "menu",
                TimeSpan.FromSeconds(20), deadline.Token);
            await ClickAsync(artifacts, children, 0, "title.create", deadline.Token);
            // A fresh create dialog drops the earlier name error.
            await WaitSnapshotAsync(artifacts, children, 0, snapshot =>
                Value(snapshot, "frontendTitleState") == "create"
                && !(Value(snapshot, "frontendStatus") ?? "").Contains("2~16"),
                TimeSpan.FromSeconds(10), deadline.Token);
            // The menu behind the dialog is hidden, so the pointer route must reject it.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ClickAsync(artifacts, children, 0, "title.find", deadline.Token));
            await TypeAsync(artifacts, children, 0, "title.create.input", "UiVerifyA", deadline.Token);
            await CaptureAsync(artifacts, children, 0, 104, 1280, 720, deadline.Token);
            await ClickAsync(artifacts, children, 0, "title.create.confirm", deadline.Token);
            JsonElement ownerParty = await WaitSnapshotAsync(artifacts, children, 0, snapshot =>
                Guid.TryParse(Value(snapshot, "frontendCurrentRoomId"), out _)
                && Number(snapshot, "frontendPartyMemberCount") == 1
                && Value(snapshot, "frontendScreen") == "party",
                TimeSpan.FromSeconds(45), deadline.Token);
            string firstRoom = Value(ownerParty, "frontendCurrentRoomId")!;
            createdRooms.Add(firstRoom);

            for (int index = 1; index < 3; index++)
            {
                await ClickAsync(artifacts, children, index, "rooms.refresh", deadline.Token);
                await WaitSnapshotAsync(artifacts, children, index, snapshot =>
                    RoomsContain(snapshot, firstRoom) && !Flag(snapshot, "frontendBusy"),
                    TimeSpan.FromSeconds(35), deadline.Token);
                await ClickAsync(artifacts, children, index, "rooms.row:" + firstRoom, deadline.Token);
                await WaitSnapshotAsync(artifacts, children, index, snapshot =>
                    Value(snapshot, "frontendCurrentRoomId") == firstRoom,
                    TimeSpan.FromSeconds(35), deadline.Token);
            }
            await WaitRoomAsync(api, parents[0].AccessToken, firstRoom,
                room => room.Members.Count == 3, TimeSpan.FromSeconds(40), deadline.Token);
            for (int index = 0; index < 3; index++)
            {
                await WaitSnapshotAsync(artifacts, children, index, snapshot =>
                    Value(snapshot, "frontendScreen") == "party"
                    && Number(snapshot, "frontendPartyMemberCount") == 3
                    && !Flag(snapshot, "frontendBusy"),
                    TimeSpan.FromSeconds(35), deadline.Token);
                await CaptureAsync(artifacts, children, index, 105,
                    index == 0 ? 1280 : 960, index == 0 ? 720 : 540, deadline.Token);
            }

            for (int index = 0; index < 3; index++)
            {
                await ClickAsync(artifacts, children, index, "party.pref:" + index, deadline.Token);
                int accountIndex = index;
                await WaitRoomAsync(api, parents[0].AccessToken, firstRoom,
                    room => room.Members.Any(member => member.AccountId == parents[accountIndex].Account.AccountId
                        && member.Preference == accountIndex), TimeSpan.FromSeconds(30), deadline.Token);
                await WaitIdleAsync(artifacts, children, index, deadline.Token);
                await ClickAsync(artifacts, children, index, "party.ready", deadline.Token);
                await WaitRoomAsync(api, parents[0].AccessToken, firstRoom,
                    room => room.Members.Any(member => member.AccountId == parents[accountIndex].Account.AccountId
                        && member.Ready), TimeSpan.FromSeconds(30), deadline.Token);
            }
            await WaitSnapshotAsync(artifacts, children, 0, snapshot =>
                !Flag(snapshot, "frontendBusy") && PartyAllReady(snapshot),
                TimeSpan.FromSeconds(30), deadline.Token);
            await ClickAsync(artifacts, children, 0, "party.start", deadline.Token);
            for (int index = 0; index < 3; index++)
            {
                JsonElement field = await WaitSnapshotAsync(artifacts, children, index, snapshot =>
                    Number(snapshot, "runScene") == 4 && ArrayLength(snapshot, "players") == 3,
                    TimeSpan.FromSeconds(100), deadline.Token);
                Assert.Equal(3, ArrayLength(field, "lobbySlots"));
            }

            await SendAsync(artifacts, children, 0, "account_return", ct: deadline.Token);
            for (int index = 0; index < 3; index++)
                await WaitSnapshotAsync(artifacts, children, index, snapshot =>
                    Flag(snapshot, "frontendSessionReady") && Value(snapshot, "frontendActiveScene") == "Title"
                    && Value(snapshot, "frontendScreen") == "title"
                    && Value(snapshot, "frontendTitleState") == "menu"
                    && !string.IsNullOrEmpty(Value(snapshot, "frontendEventSystem"))
                    && string.IsNullOrEmpty(Value(snapshot, "frontendCurrentRoomId")),
                    TimeSpan.FromSeconds(60), deadline.Token);
            await WaitRoomAbsentAsync(api, parents[0].AccessToken, firstRoom,
                TimeSpan.FromSeconds(30), deadline.Token);

            // Input must still work after a run: find rooms, then create from the rooms page.
            await ClickAsync(artifacts, children, 0, "title.find", deadline.Token);
            await WaitSnapshotAsync(artifacts, children, 0, snapshot =>
                Value(snapshot, "frontendScreen") == "rooms" && !Flag(snapshot, "frontendBusy"),
                TimeSpan.FromSeconds(35), deadline.Token);
            await TypeAsync(artifacts, children, 0, "rooms.input", "UiVerifyB", deadline.Token);
            await ClickAsync(artifacts, children, 0, "rooms.create", deadline.Token);
            JsonElement secondParty = await WaitSnapshotAsync(artifacts, children, 0, snapshot =>
                Guid.TryParse(Value(snapshot, "frontendCurrentRoomId"), out Guid id)
                && id.ToString() != firstRoom && !Flag(snapshot, "frontendBusy"),
                TimeSpan.FromSeconds(45), deadline.Token);
            string secondRoom = Value(secondParty, "frontendCurrentRoomId")!;
            createdRooms.Add(secondRoom);
            await ClickAsync(artifacts, children, 0, "party.bots", deadline.Token);
            await WaitRoomAsync(api, parents[0].AccessToken, secondRoom,
                room => room.FillWithBots, TimeSpan.FromSeconds(30), deadline.Token);
            await WaitIdleAsync(artifacts, children, 0, deadline.Token);
            await ClickAsync(artifacts, children, 0, "party.pref:2", deadline.Token);
            await WaitRoomAsync(api, parents[0].AccessToken, secondRoom,
                room => room.Members.Single().Preference == 2, TimeSpan.FromSeconds(30), deadline.Token);
            await WaitIdleAsync(artifacts, children, 0, deadline.Token);
            await ClickAsync(artifacts, children, 0, "party.ready", deadline.Token);
            await WaitRoomAsync(api, parents[0].AccessToken, secondRoom,
                room => room.Members.Single().Ready, TimeSpan.FromSeconds(30), deadline.Token);
            await WaitSnapshotAsync(artifacts, children, 0, snapshot =>
                !Flag(snapshot, "frontendBusy") && PartyAllReady(snapshot),
                TimeSpan.FromSeconds(30), deadline.Token);
            await ClickAsync(artifacts, children, 0, "party.start", deadline.Token);
            JsonElement botField = await WaitSnapshotAsync(artifacts, children, 0, snapshot =>
                Number(snapshot, "runScene") == 4 && Flag(snapshot, "lobbyBotFill")
                && ArrayLength(snapshot, "players") == 3,
                TimeSpan.FromSeconds(100), deadline.Token);
            Assert.Equal(3, ArrayLength(botField, "players"));
            await SendAsync(artifacts, children, 0, "account_return", ct: deadline.Token);
            await WaitSnapshotAsync(artifacts, children, 0, snapshot =>
                Flag(snapshot, "frontendSessionReady") && Value(snapshot, "frontendActiveScene") == "Title"
                && Value(snapshot, "frontendScreen") == "title"
                && string.IsNullOrEmpty(Value(snapshot, "frontendCurrentRoomId")),
                TimeSpan.FromSeconds(60), deadline.Token);
            await WaitRoomAbsentAsync(api, parents[0].AccessToken, secondRoom,
                TimeSpan.FromSeconds(30), deadline.Token);

            foreach (string path in Directory.EnumerateFiles(artifacts, "game-*.log"))
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                string log = await reader.ReadToEndAsync(deadline.Token);
                Assert.All(games, game => Assert.DoesNotContain(game.AccessToken, log));
                Assert.All(games, game => Assert.DoesNotContain(game.RefreshToken, log));
            }
            await File.WriteAllTextAsync(Path.Combine(artifacts, "result.txt"),
                "PASS: three account clients drove Title and in-game rooms through EventSystem pointer clicks, reached Field, returned to Title, and started a bot room again",
                deadline.Token);
        }
        finally
        {
            foreach (Process child in children)
            {
                if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); }
                child.Dispose();
            }
            if (parents.Count > 0)
            {
                foreach (string room in createdRooms)
                {
                    try { await api.CloseRoomAsync(parents[0].AccessToken, room); }
                    catch (AccountException) { }
                }
            }
            foreach (TokenReply parent in parents)
            {
                try { await api.LogoutAsync(parent.AccessToken); }
                catch (AccountException) { }
            }
        }
    }

    private static string CommandPath(string artifacts, int index) =>
        Path.Combine(artifacts, $"lobby-{index}.json");

    private static Task<JsonElement> ClickAsync(string artifacts, IReadOnlyList<Process> children,
        int index, string control, CancellationToken ct) =>
        SendAsync(artifacts, children, index, "front_click", text: control, ct: ct);

    private static Task<JsonElement> ProbeAsync(string artifacts, IReadOnlyList<Process> children,
        int index, string control, CancellationToken ct) =>
        SendAsync(artifacts, children, index, "front_probe", text: control, ct: ct);

    private static Task<JsonElement> TypeAsync(string artifacts, IReadOnlyList<Process> children,
        int index, string control, string value, CancellationToken ct) =>
        SendAsync(artifacts, children, index, "front_type", text: control + "=" + value, ct: ct);

    // Buttons are non-interactable while a request runs; wait before the next pointer click.
    private static Task<JsonElement> WaitIdleAsync(string artifacts, IReadOnlyList<Process> children,
        int index, CancellationToken ct) =>
        WaitSnapshotAsync(artifacts, children, index, snapshot => !Flag(snapshot, "frontendBusy"),
            TimeSpan.FromSeconds(30), ct);

    private static async Task<JsonElement> SendAsync(string artifacts, IReadOnlyList<Process> children,
        int index, string verb, int value = 0, string text = "", bool enabled = false,
        CancellationToken ct = default, int commandId = 1)
    {
        string path = CommandPath(artifacts, index);
        string temporary = path + ".tmp";
        string responsePath = Path.ChangeExtension(path, ".response.json");
        string lastMessage = string.Empty;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            string id = Guid.NewGuid().ToString("N");
            var requestCommand = new { commandId, verb, index = value, text, enabled };
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new
            {
                executionId = id,
                commands = verb == "snapshot" ? Array.Empty<object>() : new object[] { requestCommand },
            }), ct);
            File.Move(temporary, path, true);
            DateTime timeout = DateTime.UtcNow.AddSeconds(30);
            bool rejected = false;
            while (DateTime.UtcNow < timeout)
            {
                Assert.False(children[index].HasExited, $"Client {index} exited before {verb}");
                await Task.Delay(200, ct);
                try
                {
                    using var document = JsonDocument.Parse(await ReadSharedAsync(responsePath, ct));
                    JsonElement response = document.RootElement;
                    if (response.GetProperty("executionId").GetString() != id) continue;
                    if (verb == "snapshot") return response.GetProperty("snapshot").Clone();
                    JsonElement command = response.GetProperty("responses")[0];
                    if (!command.GetProperty("accepted").GetBoolean())
                    {
                        lastMessage = command.GetProperty("message").GetString() ?? string.Empty;
                        rejected = true;
                        break;
                    }
                    if (verb == "capture")
                    {
                        string? capturePath = command.GetProperty("capturePath").GetString();
                        Assert.False(string.IsNullOrEmpty(capturePath), $"Client {index} failed capture");
                        Assert.True(File.Exists(capturePath), $"Client {index} capture is missing");
                    }
                    return response.GetProperty("snapshot").Clone();
                }
                catch (IOException) { }
                catch (JsonException) { }
            }
            if (!rejected) throw new TimeoutException($"Client {index} did not answer {verb}");
            if (attempt < 3) await Task.Delay(400, ct);
        }
        throw new InvalidOperationException(
            $"Client {index} rejected {verb} {text} after bounded retries: {lastMessage}");
    }

    // The game replaces the response with Delete+Move; share delete so reading never blocks it.
    private static async Task<string> ReadSharedAsync(string path, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }

    private static async Task CaptureAsync(string artifacts, IReadOnlyList<Process> children,
        int index, int commandId, int width, int height, CancellationToken ct)
    {
        JsonElement snapshot = await SendAsync(artifacts, children, index, "capture",
            ct: ct, commandId: commandId);
        Assert.Equal(width, Number(snapshot, "screenWidth"));
        Assert.Equal(height, Number(snapshot, "screenHeight"));
        string pngPath = Path.Combine(artifacts, $"lobby-{index}.capture.{commandId}.png");
        byte[] png = await File.ReadAllBytesAsync(pngPath, ct);
        Assert.True(png.Length > 1000, $"Client {index} capture is empty");
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47 }, png.Take(4).ToArray());
        Assert.Equal(width, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
        Assert.Equal(height, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }

    private static async Task<JsonElement> WaitSnapshotAsync(string artifacts,
        IReadOnlyList<Process> children, int index, Func<JsonElement, bool> predicate,
        TimeSpan timeout, CancellationToken ct)
    {
        DateTime until = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < until)
        {
            JsonElement snapshot = await SendAsync(artifacts, children, index, "snapshot", ct: ct);
            if (predicate(snapshot)) return snapshot;
            await Task.Delay(400, ct);
        }
        throw new TimeoutException($"Client {index} did not reach the expected game state");
    }

    private static async Task<AccountRoom> WaitRoomAsync(AccountApi api, string access,
        string roomId, Func<AccountRoom, bool> predicate, TimeSpan timeout, CancellationToken ct)
    {
        DateTime until = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < until)
        {
            AccountRoom? room = (await api.RoomsAsync(access, ct)).Rooms
                .FirstOrDefault(item => item.RoomId == roomId);
            if (room != null && predicate(room)) return room;
            await Task.Delay(500, ct);
        }
        throw new TimeoutException("Room did not reach the expected preparation state");
    }

    private static async Task WaitRoomAbsentAsync(AccountApi api, string access,
        string roomId, TimeSpan timeout, CancellationToken ct)
    {
        DateTime until = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < until)
        {
            if (!(await api.RoomsAsync(access, ct)).Rooms.Any(room => room.RoomId == roomId)) return;
            await Task.Delay(500, ct);
        }
        throw new TimeoutException("Departed room remained in the directory");
    }

    private static bool Flag(JsonElement snapshot, string key) =>
        snapshot.TryGetProperty(key, out JsonElement value)
        && value.ValueKind == JsonValueKind.True;

    private static int Number(JsonElement snapshot, string key) =>
        snapshot.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32() : 0;

    private static string? Value(JsonElement snapshot, string key) =>
        snapshot.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static int ArrayLength(JsonElement snapshot, string key) =>
        snapshot.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength() : 0;

    private static bool RoomsContain(JsonElement snapshot, string roomId) =>
        snapshot.TryGetProperty("frontendRooms", out JsonElement rooms)
        && rooms.ValueKind == JsonValueKind.Array
        && rooms.EnumerateArray().Any(room => room.TryGetProperty("room_id", out JsonElement id)
            && id.GetString() == roomId);

    // The start button enables only after the pulsed party shows every member ready.
    private static bool PartyAllReady(JsonElement snapshot) =>
        snapshot.TryGetProperty("frontendParty", out JsonElement party)
        && party.ValueKind == JsonValueKind.Object
        && party.TryGetProperty("members", out JsonElement members)
        && members.ValueKind == JsonValueKind.Array
        && members.GetArrayLength() > 0
        && members.EnumerateArray().All(member => member.TryGetProperty("ready", out JsonElement ready)
            && ready.ValueKind == JsonValueKind.True);
}
