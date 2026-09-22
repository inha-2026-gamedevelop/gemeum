using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Geumeum.Launcher;

/// <summary>
/// 공식 Discord 초대 링크를 기본 브라우저로 연다. 로그인 상태, 계정 API 연결과 무관하게 동작한다
/// </summary>
public static class DiscordCommunity
{
    public const string InviteUrl = "https://discord.gg/8tzFAqQNvc";

    /// <summary>
    /// 브라우저를 열지 못하면 주소를 복사할 수 있게 안내만 띄운다. 런처를 중단시키지 않는다
    /// </summary>
    public static void OpenInvite(Window? owner)
    {
        try
        {
            Process.Start(new ProcessStartInfo(InviteUrl) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            string text = "브라우저를 열지 못했습니다. 아래 주소를 복사해 브라우저 주소창에 붙여넣어 주세요.\n\n"
                        + InviteUrl + "\n\n(이 창에서 Ctrl+C를 누르면 내용이 복사됩니다.)";
            if (owner != null)
                MessageBox.Show(owner, text, "그믐 공식 Discord", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show(text, "그믐 공식 Discord", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
