using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Geumeum.Accounts;

/// <summary> 런처가 실행한 동일 사용자 프로세스에만 게임 세션을 한 번 전달한다 </summary>
public sealed class GameSessionPipe : IDisposable
{
    private readonly NamedPipeServerStream _pipe;
    public string Name { get; } = "geumeum-account-" + Guid.NewGuid().ToString("N");

    public GameSessionPipe()
    {
        _pipe = new NamedPipeServerStream(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);
    }

    public async Task DeliverAsync(int childPid, Uri origin, TokenReply game, CancellationToken cancellation = default, AccountRoom? room = null)
    {
        if (game.SessionKind != "game") throw new AccountException("게임 전용 세션이 필요합니다.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        CancellationToken ct = timeout.Token;
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1, api_origin = origin.AbsoluteUri, access_token = game.AccessToken,
            refresh_token = game.RefreshToken, account_id = game.Account.AccountId,
            expires_in = game.ExpiresIn, session_expires_at = game.SessionExpiresAt,
            is_host = room?.Hosting == "listen" && room.OwnerId == game.Account.AccountId,
            room_id = room?.RoomId, address = room?.Address, port = room?.Port ?? 0,
        });
        try
        {
            await _pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
            if (!GetNamedPipeClientProcessId(_pipe.SafePipeHandle, out uint actual) || actual != childPid)
                throw new AccountException("게임 연결 프로세스를 확인하지 못했습니다.");
            byte[] header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
            await _pipe.WriteAsync(header, ct).ConfigureAwait(false);
            await _pipe.WriteAsync(payload, ct).ConfigureAwait(false);
            await _pipe.FlushAsync(ct).ConfigureAwait(false);
            byte[] ack = new byte[1];
            await _pipe.ReadExactlyAsync(ack, ct).ConfigureAwait(false);
            if (ack[0] != 1) throw new AccountException("게임 세션 수신 확인에 실패했습니다.");
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            throw new AccountException("게임이 계정 세션을 받지 못했습니다. 새 게임 빌드인지 확인해 주세요.");
        }
        finally { Array.Clear(payload); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);

    public void Dispose() => _pipe.Dispose();
}
