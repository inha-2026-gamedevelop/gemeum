// System
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

// Unity
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Geumeum.EditorTools
{
    /// <summary>
    /// 툴바의 그믐, 빌드 메뉴. 빌드 옵션을 사람마다 다르게 잡지 않도록 한 곳에 고정한다.
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

        // 3인 테스트 실행 스크립트. 빌드 폴더째로 팀원에게 전달해도 바로 접속할 수 있게 같이 넣는다.
        // 릴리스에는 넣지 않는다 - 실행 인자로 자동 접속하는 경로는 개발 빌드에만 있다
        private const string TOOLS_DIR = "tools";

        private static readonly string[] DEVELOPMENT_EXTRA_FILES =
        {
            "run-host.bat",
            "run-client.bat",
        };

        private const int ORDER_DEVELOPMENT = 0;
        private const int ORDER_RELEASE     = 1;
        private const int ORDER_OPEN_BUILD  = 20;
        private const int ORDER_OPEN_LOG    = 21;

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

            UnityEngine.Debug.Log($"[그믐] {outputDir} 빌드 성공 - {summary.totalTime.TotalSeconds:F1}초, "
                + $"{summary.totalSize / 1048576f:F1}MB, 씬 {scenes.Length}개\n{path}");
        }

        // 복사에 실패해도 빌드 자체는 성공이다. 막지 않고 경고만 남긴다
        private static void CopyExtraFiles(string outputPath)
        {
            string toolsPath = Path.Combine(ProjectRoot(), TOOLS_DIR);

            for (int i = 0; i < DEVELOPMENT_EXTRA_FILES.Length; ++i)
            {
                string source = Path.Combine(toolsPath, DEVELOPMENT_EXTRA_FILES[i]);

                if (!File.Exists(source))
                {
                    UnityEngine.Debug.LogWarning($"[그믐] 빌드에 넣을 파일이 없다: {source}");
                    continue;
                }

                try
                {
                    File.Copy(source, Path.Combine(outputPath, DEVELOPMENT_EXTRA_FILES[i]), true);
                }
                catch (IOException exception)
                {
                    UnityEngine.Debug.LogWarning($"[그믐] {DEVELOPMENT_EXTRA_FILES[i]} 복사 실패: {exception.Message}");
                }
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
        private static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        private static void OpenFolder(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
    }
}
