using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Geumeum.Network
{
    /// <summary> 자식 게임이 지정된 런처 프로세스에서만 세션을 받는다. 토큰은 인자/파일에 넣지 않는다 </summary>
    public static class GameSessionPipeClient
    {
        public static bool IsValidName(string name)
        {
            const string prefix = "geumeum-account-";
            if (name == null || !name.StartsWith(prefix, StringComparison.Ordinal) || name.Length != prefix.Length + 32)
            {
                return false;
            }

            for (int i = prefix.Length; i < name.Length; ++i)
            {
                if (!((name[i] >= '0' && name[i] <= '9') || (name[i] >= 'a' && name[i] <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }

        public static async Task<string> ReceiveAsync(string name, int launcherPid, CancellationToken ct)
        {
            if (!IsValidName(name) || launcherPid <= 0)
            {
                throw new IOException("Invalid account channel");
            }

            using (var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
            using (ct.Register(() => pipe.Dispose()))
            {
                await pipe.ConnectAsync(120000, ct).ConfigureAwait(false);
                if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint actual) || actual != launcherPid)
                {
                    throw new IOException("Invalid launcher process");
                }

                byte[] header = new byte[4];
                await ReadExactlyAsync(pipe, header, ct).ConfigureAwait(false);
                int length = header[0] | header[1] << 8 | header[2] << 16 | header[3] << 24;
                if (length < 1 || length > 32768)
                {
                    throw new IOException("Invalid account payload size");
                }

                byte[] payload = new byte[length];
                try
                {
                    await ReadExactlyAsync(pipe, payload, ct).ConfigureAwait(false);
                    string json = new UTF8Encoding(false, true).GetString(payload);
                    await pipe.WriteAsync(new byte[] { 1 }, 0, 1, ct).ConfigureAwait(false);
                    await pipe.FlushAsync(ct).ConfigureAwait(false);
                    return json;
                }
                finally { Array.Clear(payload, 0, payload.Length); }
            }
        }

        private static async Task ReadExactlyAsync(Stream stream, byte[] bytes, CancellationToken ct)
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = await stream.ReadAsync(bytes, offset, bytes.Length - offset, ct).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }

                offset += read;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    }
}
