// System
using System.Collections.Generic;
using System.Text;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.EditorTools
{
    /// <summary>
    /// 툴바의 그믐 · 씬 · 검사 메뉴.
    /// 서버 부트 게이트가 잡아내는 데이터 결함을 Play 모드에 들어가기 전에 먼저 확인한다
    /// </summary>
    public static class GeumeumCheckMenu
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string MENU_SCENE = GeumeumBuildMenu.MENU_ROOT + "씬/";
        private const string MENU_CHECK = GeumeumBuildMenu.MENU_ROOT + "검사/";
        private const string MENU_DATA  = GeumeumBuildMenu.MENU_ROOT + "데이터/";

        private const string SCENE_DIR    = "Assets/00.Scenes/";
        private const string SCENE_EXT    = ".unity";
        private const string GAME_DB_PATH = "Assets/09.Data/Resources/GameDB.asset";

        private const int ORDER_SCENE_MAIN   = 40;
        private const int ORDER_SCENE_COMBAT = 41;
        private const int ORDER_CHECK_DATA   = 60;
        private const int ORDER_CHECK_SCENE  = 61;
        private const int ORDER_DATA_SELECT  = 80;

        private static readonly List<string> G_Problems = new List<string>();

        /****************************************
        *                 Scene
        ****************************************/

        [MenuItem(MENU_SCENE + "Main 열기", false, ORDER_SCENE_MAIN)]
        private static void OpenMain()
        {
            OpenScene(Constants.Scene.MAIN);
        }

        [MenuItem(MENU_SCENE + "Dev_Combat 열기", false, ORDER_SCENE_COMBAT)]
        private static void OpenDevCombat()
        {
            OpenScene(Constants.Scene.DEV_COMBAT);
        }

        private static void OpenScene(string sceneName)
        {
            // 저장하지 않고 씬을 바꾸면 편집 내용이 사라진다
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            string path = SCENE_DIR + sceneName + SCENE_EXT;
            if (!System.IO.File.Exists(path))
            {
                EditorUtility.DisplayDialog("씬 없음", path + " 이 아직 없다.", "확인");
                return;
            }

            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }

        /****************************************
        *                 Check
        ****************************************/

        /// <summary> 런을 시작할 수 있는 데이터인지 검사한다. 서버 부트 게이트와 같은 불변식을 본다 </summary>
        [MenuItem(MENU_CHECK + "런 데이터 검사", false, ORDER_CHECK_DATA)]
        private static void CheckRunData()
        {
            G_Problems.Clear();

            var database = AssetDatabase.LoadAssetAtPath<GameDatabaseSO>(GAME_DB_PATH);
            if (database == null)
            {
                Report("런 데이터 검사", "GameDB 에셋을 찾지 못했다: " + GAME_DB_PATH);
                return;
            }

            database.CollectMissingReferences(G_Problems);

            // 서버 부트 게이트와 같은 규칙을 쓴다 — 여기서 통과해도 부트에서 걸리면 검사 의미가 없다
            GeumeumBootValidator.ValidateData(database.Barrier, database.LevelCurve, database.Spawn, G_Problems);

            Report("런 데이터 검사", null);
        }

        /// <summary> 현재 씬에 서버를 열 수 있는 필수 구성이 있는지 검사한다. 서버 부트 게이트와 같은 규칙을 쓴다 </summary>
        [MenuItem(MENU_CHECK + "씬 필수 구성 검사", false, ORDER_CHECK_SCENE)]
        private static void CheckSceneSetup()
        {
            G_Problems.Clear();

            // Main만 로비·런 매니저·프레임 기록기까지 요구한다, 그 외 씬은 Dev 프로필로 완화한다
            bool isMain = (EditorSceneManager.GetActiveScene().name == Constants.Scene.MAIN);
            var profile = isMain ? GeumeumBootValidator.SceneProfile.Main : GeumeumBootValidator.SceneProfile.DevScene;
            GeumeumBootValidator.ValidateScene(profile, G_Problems);

            var manager = Object.FindAnyObjectByType<GeumeumNetworkManager>(FindObjectsInactive.Include);
            if ((manager != null) && (manager.playerPrefab == null))
            {
                G_Problems.Add("네트워크 매니저에 플레이어 프리팹이 등록되지 않았다");
            }

            Report("씬 필수 구성 검사", null);
        }

        /****************************************
        *                 Data
        ****************************************/

        [MenuItem(MENU_DATA + "GameDB 선택", false, ORDER_DATA_SELECT)]
        private static void SelectGameDatabase()
        {
            var database = AssetDatabase.LoadAssetAtPath<GameDatabaseSO>(GAME_DB_PATH);
            if (database == null)
            {
                EditorUtility.DisplayDialog("GameDB 없음", GAME_DB_PATH + " 을 찾지 못했다.", "확인");
                return;
            }

            Selection.activeObject = database;
            EditorGUIUtility.PingObject(database);
        }

        /****************************************
        *                Methods
        ****************************************/

        private static void Report(string title, string fatalMessage)
        {
            if (fatalMessage != null)
            {
                Debug.LogError("[그믐] " + title + " 실패 — " + fatalMessage);
                EditorUtility.DisplayDialog(title, fatalMessage, "확인");
                return;
            }

            if (G_Problems.Count == 0)
            {
                Debug.Log("[그믐] " + title + " 통과");
                EditorUtility.DisplayDialog(title, "문제 없음", "확인");
                return;
            }

            var text = new StringBuilder();
            for (int i = 0; i < G_Problems.Count; ++i)
            {
                text.AppendLine("- " + G_Problems[i]);
            }

            Debug.LogError("[그믐] " + title + " 실패 " + G_Problems.Count + "건\n" + text);
            EditorUtility.DisplayDialog(title, G_Problems.Count + "건\n\n" + text, "확인");
        }
    }
}
