// System
using System.Diagnostics;
using System.IO;

// Unity
using UnityEditor;
using UnityEngine;

namespace Geumeum.EditorTools
{
    /// <summary>
    /// 툴바의 그믐 - 런 테스트 메뉴
    /// tools 폴더의 실행 스크립트를 Unity 밖에서 직접 찾아 실행하던 것을 여기로 모았다
    /// 스크립트 자체는 그대로 두고 호출만 옮긴다 - 다른 PC에서 배치 파일로 직접 돌리는 경로가 살아 있어야 한다
    /// </summary>
    public static class GeumeumRunTestMenu
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string MENU_RUN = GeumeumBuildMenu.MENU_ROOT + "런 테스트/";

        private const string TOOLS_DIR       = "tools";
        private const string BUILD_ROOT      = "Builds";
        private const string DEVELOPMENT_DIR = "Development";
        private const string LOG_DIR         = "logs";
        private const string EXECUTABLE_NAME = "gemeum.exe";

        private const string SCRIPT_LOCAL   = "run-3clients.ps1";
        private const string SCRIPT_HOST    = "run-host.bat";
        private const string SCRIPT_CLIENT  = "run-client.bat";
        private const string SCRIPT_STEAM_HOST   = "run-steam-host.bat";
        private const string SCRIPT_STEAM_CLIENT = "run-steam-client.bat";
        private const string SCRIPT_STEAM_LOBBY  = "run-steam-lobby.bat";

        private const int PLAYER_COUNT_THREE = 3;
        private const int PLAYER_COUNT_TWO   = 2;

        private const int ORDER_LOCAL_THREE = 0;
        private const int ORDER_LOCAL_TWO   = 1;
        private const int ORDER_KILL        = 2;
        private const int ORDER_KCP_HOST    = 20;
        private const int ORDER_KCP_CLIENT  = 21;
        private const int ORDER_STEAM_HOST   = 40;
        private const int ORDER_STEAM_CLIENT = 41;
        private const int ORDER_STEAM_LOBBY  = 42;
        private const int ORDER_OPEN_LOG     = 60;

        /****************************************
        *              Local Test
        ****************************************/

        /// <summary> 한 PC에서 3인(M2). 다른 PC 3인(M3)이 아니라는 것을 이름으로 구분한다 </summary>
        [MenuItem(MENU_RUN + "이 PC에서 3인 실행 (M2)", false, ORDER_LOCAL_THREE)]
        private static void RunLocalThree()
        {
            RunPowerShell(SCRIPT_LOCAL, $"-Count {PLAYER_COUNT_THREE}");
        }

        [MenuItem(MENU_RUN + "이 PC에서 2인 실행 (M1)", false, ORDER_LOCAL_TWO)]
        private static void RunLocalTwo()
        {
            RunPowerShell(SCRIPT_LOCAL, $"-Count {PLAYER_COUNT_TWO}");
        }

        /// <summary> 창을 하나씩 닫지 않아도 되게 한 번에 정리한다 </summary>
        [MenuItem(MENU_RUN + "실행한 빌드 전부 종료", false, ORDER_KILL)]
        private static void KillRunning()
        {
            RunPowerShell(SCRIPT_LOCAL, "-Kill", requiresBuild: false);
        }

        /****************************************
        *               Other PC
        ****************************************/

        [MenuItem(MENU_RUN + "KCP 호스트 실행 (다른 PC 3인)", false, ORDER_KCP_HOST)]
        private static void RunKcpHost()
        {
            RunBatch(SCRIPT_HOST);
        }

        [MenuItem(MENU_RUN + "KCP 클라이언트 실행", false, ORDER_KCP_CLIENT)]
        private static void RunKcpClient()
        {
            RunBatch(SCRIPT_CLIENT);
        }

        [MenuItem(MENU_RUN + "Steam 호스트 실행", false, ORDER_STEAM_HOST)]
        private static void RunSteamHost()
        {
            RunBatch(SCRIPT_STEAM_HOST);
        }

        [MenuItem(MENU_RUN + "Steam 클라이언트 실행", false, ORDER_STEAM_CLIENT)]
        private static void RunSteamClient()
        {
            RunBatch(SCRIPT_STEAM_CLIENT);
        }

        [MenuItem(MENU_RUN + "Steam 로비 실행", false, ORDER_STEAM_LOBBY)]
        private static void RunSteamLobby()
        {
            RunBatch(SCRIPT_STEAM_LOBBY);
        }

        /// <summary> 실행 로그는 빌드 옆 logs 폴더에 쌓인다. 프레임 로그(persistentDataPath)와 다른 곳이다 </summary>
        [MenuItem(MENU_RUN + "실행 로그 폴더 열기", false, ORDER_OPEN_LOG)]
        private static void OpenRunLogFolder()
        {
            GeumeumBuildMenu.OpenFolder(
                Path.Combine(GeumeumBuildMenu.ProjectRoot(), BUILD_ROOT, DEVELOPMENT_DIR, LOG_DIR));
        }

        /****************************************
        *                Methods
        ****************************************/

        private static void RunPowerShell(string scriptName, string arguments, bool requiresBuild = true)
        {
            string script = ToolPath(scriptName);
            if ((script == null) || ((requiresBuild) && (!HasDevelopmentBuild())))
            {
                return;
            }

            // ExecutionPolicy Bypass가 없으면 서명 정책 때문에 사람마다 되고 안 되고가 갈린다
            Launch("powershell.exe", $"-ExecutionPolicy Bypass -File \"{script}\" {arguments}");
        }

        private static void RunBatch(string scriptName)
        {
            string script = ToolPath(scriptName);
            if ((script == null) || (!HasDevelopmentBuild()))
            {
                return;
            }

            // 배치가 자기 창에서 안내를 출력하고 pause로 멈추므로 콘솔을 띄운 채로 넘긴다
            Launch("cmd.exe", $"/c start \"\" \"{script}\"");
        }

        private static void Launch(string fileName, string arguments)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = fileName,
                Arguments       = arguments,
                WorkingDirectory = GeumeumBuildMenu.ProjectRoot(),
                UseShellExecute = true,
            });
        }

        private static string ToolPath(string scriptName)
        {
            string path = Path.Combine(GeumeumBuildMenu.ProjectRoot(), TOOLS_DIR, scriptName);
            if (File.Exists(path))
            {
                return path;
            }

            EditorUtility.DisplayDialog("실행 스크립트 없음", path + " 이 없다.", "확인");
            return null;
        }

        /// <summary>
        /// 실행 인자(-host, -platformid 등)는 개발 빌드에서만 동작한다
        /// 릴리스 빌드로 돌리면 아무 일도 안 일어나는데 원인이 안 보인다
        /// </summary>
        private static bool HasDevelopmentBuild()
        {
            string exe = Path.Combine(
                GeumeumBuildMenu.ProjectRoot(), BUILD_ROOT, DEVELOPMENT_DIR, EXECUTABLE_NAME);

            if (File.Exists(exe))
            {
                return true;
            }

            bool build = EditorUtility.DisplayDialog(
                "개발 빌드 없음",
                exe + " 이 없다.\n\n실행 인자는 개발 빌드에서만 동작한다. 지금 빌드할까?",
                "개발용 빌드",
                "취소");

            if (build)
            {
                EditorApplication.ExecuteMenuItem(GeumeumBuildMenu.MENU_ROOT + "빌드/개발용 빌드");
            }

            return false;
        }
    }
}
