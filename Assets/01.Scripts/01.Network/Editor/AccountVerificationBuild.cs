using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Geumeum.Network.Editor
{
    /// <summary> 계정 IPC/3인 KCP 검증 전용 산출물. 씬과 Build Settings는 수정하지 않는다 </summary>
    public static class AccountVerificationBuild
    {
        [MenuItem("그믐/빌드/계정 연결 검증 빌드")]
        public static void Build()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/AccountVerification"));
            string[] scenes = new[] { "Assets/00.Scenes/Dev_Combat.unity" }
                .Concat(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path)).Distinct().ToArray();
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes, locationPathName = Path.Combine(root, "gemeum.exe"),
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development,
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new IOException("Account verification build failed");
            }

            string streaming = Path.Combine(root, "gemeum_Data/StreamingAssets");
            Directory.CreateDirectory(streaming);
            File.WriteAllText(Path.Combine(streaming, BuildIdentity.FILE_NAME), BuildIdentity.Serialize(new BuildIdentity.Data
            {
                releaseId = "account-test-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),
                displayVersion = PlayerSettings.bundleVersion, protocolVersion = BuildIdentity.PROTOCOL_VERSION,
                contentVersion = "local-verification",
            }));
            Debug.Log("[AccountVerificationBuild] PASS: " + root);
        }
    }
}
