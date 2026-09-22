// System
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

// Unity
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// 프로젝트 내부
using Geumeum.Network;

namespace Geumeum.EditorTools
{
    /// <summary>
    /// 툴바의 그믐, 빌드 메뉴. 빌드 옵션을 사람마다 다르게 잡지 않도록 한 곳에 고정한다
    /// 개발 빌드에만 DEVELOPMENT_BUILD가 정의되어 FrameRecorder 같은 측정 도구가 함께 들어간다
    /// </summary>
    public static class GeumeumBuildMenu
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string MENU_ROOT = "그믐/";

        private const string MENU_BUILD = MENU_ROOT + "빌드/";

        private const string BUILD_ROOT      = "Builds";
        private const string DEVELOPMENT_DIR = "Development";
        private const string RELEASE_DIR     = "Release";
        private const string EXECUTABLE_NAME = "gemeum.exe";

        // 실행에 필요한 스크립트만 복사한다. tools의 제작용 소스, 의존성은 빌드에 넣지 않는다
        // 릴리스에는 넣지 않는다 - 실행 인자로 자동 접속하는 경로는 개발 빌드에만 있다
        private const string TOOLS_DIR = "tools";
        private static readonly string[] DEVELOPMENT_TOOLS =
        {
            "run-3clients.ps1", "run-client.bat", "run-host.bat",
            "run-steam-client.bat", "run-steam-host.bat", "run-steam-lobby.bat",
            "show-steam-client-result.ps1", "show-steam-host-id.ps1",
        };

        // SteamAPI.Init은 exe 옆의 이 파일에서 AppID를 읽는다. 없으면 Steam 경로가 초기화에서 막힌다
        // tools가 아니라 프로젝트 루트에 있어 따로 복사한다. 릴리스에는 넣지 않는다 - 실제 AppID로 출시하면 불필요하다
        private const string STEAM_APPID_FILE = "steam_appid.txt";

        // 로비와 인증에서 사용하는 빌드 정보를 패키징 manifest와 대조한다
        private const string DATA_DIR_SUFFIX   = "_Data";
        private const string STREAMING_DIR     = "StreamingAssets";
        private const string RELEASE_ID_PREFIX = "win64";

        private const string MENU_ALLOW_MISMATCH  = MENU_BUILD + "에디터 빌드 불일치 허용";
        private const string PREF_ALLOW_MISMATCH  = "Geumeum.AllowBuildMismatch";

        private const int ORDER_DEVELOPMENT    = 0;
        private const int ORDER_RELEASE        = 1;
        private const int ORDER_ALLOW_MISMATCH = 10;
        private const int ORDER_OPEN_BUILD     = 20;
        private const int ORDER_OPEN_LOG       = 21;

        private const BuildTarget      BUILD_TARGET       = BuildTarget.StandaloneWindows64;
        private const BuildTargetGroup BUILD_TARGET_GROUP = BuildTargetGroup.Standalone;

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem(MENU_BUILD + "개발용 빌드", false, ORDER_DEVELOPMENT)]
        private static void BuildDevelopment()
        {
            // 프로파일러 자동 연결은 켜지 않는다, 3인 테스트에서 호스트만 느려져 측정이 왜곡된다
            Build(DEVELOPMENT_DIR, BuildOptions.Development | BuildOptions.AllowDebugging);
        }

        [MenuItem(MENU_BUILD + "릴리스로 빌드", false, ORDER_RELEASE)]
        private static void BuildRelease()
        {
            Build(RELEASE_DIR, BuildOptions.None);
        }

        // 에디터 호스트에 빌드 클라이언트를 붙일 때만 켠다. 에디터 releaseId는 항상 editor라 평소엔 빌드와 붙지 않는다
        [MenuItem(MENU_ALLOW_MISMATCH, false, ORDER_ALLOW_MISMATCH)]
        private static void ToggleAllowMismatch()
        {
            bool next = !EditorPrefs.GetBool(PREF_ALLOW_MISMATCH, false);
            EditorPrefs.SetBool(PREF_ALLOW_MISMATCH, next);
            BuildIdentity.EditorAllowMismatch = next;
            UnityEngine.Debug.Log($"[그믐] 에디터 빌드 불일치 허용: {(next ? "켬" : "끔")}");
        }

        [MenuItem(MENU_ALLOW_MISMATCH, true)]
        private static bool ValidateAllowMismatch()
        {
            Menu.SetChecked(MENU_ALLOW_MISMATCH, EditorPrefs.GetBool(PREF_ALLOW_MISMATCH, false));
            return true;
        }

        [InitializeOnLoadMethod]
        private static void RestoreAllowMismatch()
        {
            BuildIdentity.EditorAllowMismatch = EditorPrefs.GetBool(PREF_ALLOW_MISMATCH, false);
        }

        [MenuItem(MENU_BUILD + "빌드 폴더 열기", false, ORDER_OPEN_BUILD)]
        private static void OpenBuildFolder()
        {
            OpenFolder(Path.Combine(ProjectRoot(), BUILD_ROOT));
        }

        [MenuItem(MENU_BUILD + "프레임 로그 폴더 열기", false, ORDER_OPEN_LOG)]
        private static void OpenFrameLogFolder()
        {
            OpenFolder(Application.persistentDataPath);
        }

        private static void Build(string outputDir, BuildOptions options)
        {
            string[] scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                EditorUtility.DisplayDialog("빌드 취소", "Build Settings에 활성화된 씬이 없다.", "확인");
                return;
            }

            // 저장하지 않으면 편집 중인 내용이 빠진 채로 빌드된다
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            string path = Path.Combine(ProjectRoot(), BUILD_ROOT, outputDir, EXECUTABLE_NAME);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            var buildOptions = new BuildPlayerOptions
            {
                scenes           = scenes,
                locationPathName = path,
                target           = BUILD_TARGET,
                targetGroup      = BUILD_TARGET_GROUP,
                options          = options,
            };

            BuildReport  report  = BuildPipeline.BuildPlayer(buildOptions);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                UnityEngine.Debug.LogError($"[그믐] {outputDir} 빌드 실패: {summary.result} (에러 {summary.totalErrors}건)");
                return;
            }

            if (outputDir == DEVELOPMENT_DIR)
            {
                CopyExtraFiles(Path.GetDirectoryName(path));
            }
            CleanNonRuntimeArtifacts(Path.GetDirectoryName(path));

            // 실패하면 빌드가 어떤 배포와도 붙지 않는다. 조용히 넘기지 않고 빌드 실패로 본다
            if (!TryWriteBuildIdentity(path, out string releaseId))
            {
                return;
            }

            UnityEngine.Debug.Log($"[그믐] {outputDir} 빌드 성공 - {summary.totalTime.TotalSeconds:F1}초, "
                + $"{summary.totalSize / 1048576f:F1}MB, 씬 {scenes.Length}개, releaseId={releaseId}\n{path}");
        }

        // 빌드마다 새 releaseId를 찍는다. 같은 소스라도 다른 빌드끼리는 붙지 않는 것이 계약이다
        private static bool TryWriteBuildIdentity(string executablePath, out string releaseId)
        {
            releaseId = $"{RELEASE_ID_PREFIX}-{DateTime.Now:yyyyMMdd-HHmm}";

            string dataDir = Path.Combine(Path.GetDirectoryName(executablePath),
                Path.GetFileNameWithoutExtension(executablePath) + DATA_DIR_SUFFIX, STREAMING_DIR);
            string target  = Path.Combine(dataDir, BuildIdentity.FILE_NAME);

            var data = new BuildIdentity.Data
            {
                releaseId       = releaseId,
                displayVersion  = PlayerSettings.bundleVersion,
                protocolVersion = BuildIdentity.PROTOCOL_VERSION,
                contentVersion  = SourceCommit(),
            };

            try
            {
                Directory.CreateDirectory(dataDir);
                File.WriteAllText(target, BuildIdentity.Serialize(data));
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                UnityEngine.Debug.LogError($"[그믐] 빌드 정체성 파일을 쓰지 못했다: {target}\n{exception.Message}");
                return false;
            }
        }

        // 어느 커밋으로 만든 빌드인지 남긴다. 커밋하지 않은 변경이 있으면 표시한다
        private static string SourceCommit()
        {
            string commit = RunGit("rev-parse --short=12 HEAD");
            if (string.IsNullOrEmpty(commit))
            {
                UnityEngine.Debug.LogWarning("[그믐] git 커밋을 읽지 못해 contentVersion을 unknown으로 둔다");
                return "unknown";
            }

            string dirty = RunGit("status --porcelain --untracked-files=no");
            return string.IsNullOrEmpty(dirty) ? commit : commit + "-dirty";
        }

        private static string RunGit(string arguments)
        {
            var info = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory       = ProjectRoot(),
                RedirectStandardOutput = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };

            try
            {
                using (Process process = Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    return (process.ExitCode == 0) ? output : null;
                }
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                UnityEngine.Debug.LogWarning($"[그믐] git 실행 실패: {exception.Message}");
                return null;
            }
        }

        // 복사에 실패해도 빌드 자체는 성공이다. 막지 않고 경고만 남긴다
        private static void CopyExtraFiles(string outputPath)
        {
            CopyIfExists(Path.Combine(ProjectRoot(), STEAM_APPID_FILE), Path.Combine(outputPath, STEAM_APPID_FILE));

            int copied = CopyTools(Path.Combine(ProjectRoot(), TOOLS_DIR), outputPath);
            UnityEngine.Debug.Log($"[그믐] 실행 스크립트 {copied}개를 빌드 폴더에 넣었다");
        }

        // 배치 파일과 보조 PowerShell은 exe 옆에서 서로를 찾는다
        private static int CopyTools(string toolsPath, string outputPath)
        {
            if (!Directory.Exists(toolsPath))
            {
                UnityEngine.Debug.LogWarning($"[그믐] 실행 스크립트 폴더가 없다: {toolsPath}");
                return 0;
            }

            int copied = 0;
            foreach (string name in DEVELOPMENT_TOOLS)
            {
                string source = Path.Combine(toolsPath, name);
                string destination = Path.Combine(outputPath, name);

                if (TryCopy(source, destination))
                {
                    ++copied;
                }
            }

            return copied;
        }

        // Unity는 같은 출력 폴더에 남은 예전 복사본을 지우지 않으므로 성공한 빌드 뒤 정리한다
        private static void CleanNonRuntimeArtifacts(string outputPath)
        {
            string root = Path.GetFullPath(Path.Combine(ProjectRoot(), BUILD_ROOT)) + Path.DirectorySeparatorChar;
            string output = Path.GetFullPath(outputPath);
            if (!output.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                || (File.GetAttributes(output) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("빌드 정리 경로가 Builds 내부의 일반 폴더가 아니다: " + output);
            }

            foreach (string name in new[] { "teaser", "gemeum_BurstDebugInformation_DoNotShip", "gemeum_BackUpThisFolder_ButDontShipItWithYourGame" })
            {
                string target = Path.GetFullPath(Path.Combine(output, name));
                if (!target.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("빌드 정리 대상 경로가 잘못됐다: " + target);
                }

                if (Directory.Exists(target))
                {
                    if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new IOException("빌드 정리 대상이 링크다: " + target);
                    }

                    Directory.Delete(target, true);
                }
            }
        }

        private static void CopyIfExists(string source, string destination)
        {
            if (!File.Exists(source))
            {
                UnityEngine.Debug.LogWarning($"[그믐] 빌드에 넣을 파일이 없다: {source}");
                return;
            }

            TryCopy(source, destination);
        }

        private static bool TryCopy(string source, string destination)
        {
            try
            {
                File.Copy(source, destination, true);
                return true;
            }
            catch (IOException exception)
            {
                UnityEngine.Debug.LogWarning($"[그믐] {Path.GetFileName(source)} 복사 실패: {exception.Message}");
                return false;
            }
        }

        private static string[] EnabledScenes()
        {
            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                {
                    scenes.Add(scene.path);
                }
            }

            return scenes.ToArray();
        }

        // Assets의 부모가 프로젝트 루트다, 빌드 결과는 Assets 밖에 둔다
        /// <summary> 저장소 루트. tools 폴더와 Builds 폴더의 기준이라 다른 메뉴도 쓴다 </summary>
        internal static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        /// <summary> 탐색기로 폴더를 연다. 없으면 만든다 </summary>
        internal static void OpenFolder(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
    }
}
