// System
using System.Collections.Generic;
using System.Text;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.EditorTools
{
    /// <summary>
    /// 툴바의 그믐, 씬, 검사 메뉴.
    /// 서버 부트 게이트가 잡아내는 데이터 결함을 Play 모드에 들어가기 전에 먼저 확인한다
    /// </summary>
    public static class GeumeumCheckMenu
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string MENU_SCENE         = GeumeumBuildMenu.MENU_ROOT + "씬/";
        private const string MENU_SCENE_RELEASE = MENU_SCENE + "런타임/";
        private const string MENU_SCENE_DEV     = MENU_SCENE + "개발/";

        // 런타임 씬은 Assets/00.Scenes/Release/ 아래에, 시스템 개발 씬은 Dev_System/ 아래에 있다
        private const string RELEASE_SUBDIR    = "Release/";
        private const string DEV_SYSTEM_SUBDIR = "Dev_System/";
        private const string MENU_CHECK = GeumeumBuildMenu.MENU_ROOT + "검사/";
        private const string MENU_DATA  = GeumeumBuildMenu.MENU_ROOT + "데이터/";

        private const string PREFAB_DIR   = "Assets/02.Prefabs";
        private const string PREFAB_FILTER = "t:Prefab";

        private const string SCENE_DIR    = "Assets/00.Scenes/";
        private const string SCENE_EXT    = ".unity";
        private const string GAME_DB_PATH = "Assets/09.Data/Resources/GameDB.asset";

        private const int ORDER_SCENE_MAIN   = 40;
        private const int ORDER_SCENE_COMBAT = 41;

        // 런 순서 그대로 둔다. 알파벳순으로 두면 어디로 이어지는지 안 보인다
        private const int ORDER_SCENE_BOOT           = 0;
        private const int ORDER_SCENE_TITLE          = 1;
        private const int ORDER_SCENE_LOBBY          = 2;
        private const int ORDER_SCENE_FIELD          = 3;
        private const int ORDER_SCENE_BOSS_ENTRANCE  = 4;
        private const int ORDER_SCENE_BOSS_ARENA     = 5;
        private const int ORDER_SCENE_RESULT         = 6;

        private const int ORDER_SCENE_DEV_AI        = 0;
        private const int ORDER_SCENE_DEV_CHARACTER = 1;
        private const int ORDER_SCENE_DEV_BARRIER   = 20;
        private const int ORDER_SCENE_DEV_LOBBY     = 21;
        private const int ORDER_SCENE_DEV_TITLE     = 22;
        private const int ORDER_SCENE_DEV_HUD       = 23;
        private const int ORDER_SCENE_DEV_WEATHER   = 40;
        private const int ORDER_CHECK_DATA   = 60;
        private const int ORDER_CHECK_SCENE  = 61;
        private const int ORDER_CHECK_POOL   = 62;
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

        [MenuItem(MENU_SCENE_RELEASE + "Boot 열기", false, ORDER_SCENE_BOOT)]
        private static void OpenBoot() { OpenScene(RELEASE_SUBDIR + Constants.Scene.BOOT); }

        [MenuItem(MENU_SCENE_RELEASE + "Title 열기", false, ORDER_SCENE_TITLE)]
        private static void OpenTitle() { OpenScene(RELEASE_SUBDIR + Constants.Scene.TITLE); }

        [MenuItem(MENU_SCENE_RELEASE + "Lobby 열기", false, ORDER_SCENE_LOBBY)]
        private static void OpenLobby() { OpenScene(RELEASE_SUBDIR + Constants.Scene.LOBBY); }

        [MenuItem(MENU_SCENE_RELEASE + "Field 열기", false, ORDER_SCENE_FIELD)]
        private static void OpenField() { OpenScene(RELEASE_SUBDIR + Constants.Scene.FIELD); }

        [MenuItem(MENU_SCENE_RELEASE + "BossEntrance 열기", false, ORDER_SCENE_BOSS_ENTRANCE)]
        private static void OpenBossEntrance() { OpenScene(RELEASE_SUBDIR + Constants.Scene.BOSS_ENTRANCE); }

        [MenuItem(MENU_SCENE_RELEASE + "Boss_Arena 열기", false, ORDER_SCENE_BOSS_ARENA)]
        private static void OpenBossArena() { OpenScene(RELEASE_SUBDIR + Constants.Scene.BOSS_ARENA); }

        [MenuItem(MENU_SCENE_RELEASE + "Result 열기", false, ORDER_SCENE_RESULT)]
        private static void OpenResult() { OpenScene(RELEASE_SUBDIR + Constants.Scene.RESULT); }

        [MenuItem(MENU_SCENE_DEV + "Dev_AI 열기", false, ORDER_SCENE_DEV_AI)]
        private static void OpenDevAI() { OpenScene(Constants.Scene.DEV_AI); }

        [MenuItem(MENU_SCENE_DEV + "Dev_Character 열기", false, ORDER_SCENE_DEV_CHARACTER)]
        private static void OpenDevCharacter() { OpenScene("Dev_Character"); }

        [MenuItem(MENU_SCENE_DEV + "Dev_Barrier 열기", false, ORDER_SCENE_DEV_BARRIER)]
        private static void OpenDevBarrier() { OpenScene(DEV_SYSTEM_SUBDIR + "Dev_Barrier"); }

        [MenuItem(MENU_SCENE_DEV + "Dev_Lobby 열기", false, ORDER_SCENE_DEV_LOBBY)]
        private static void OpenDevLobby() { OpenScene(DEV_SYSTEM_SUBDIR + "Dev_Lobby"); }

        [MenuItem(MENU_SCENE_DEV + "Dev_Title 열기", false, ORDER_SCENE_DEV_TITLE)]
        private static void OpenDevTitle() { OpenScene(DEV_SYSTEM_SUBDIR + "Dev_Title"); }

        [MenuItem(MENU_SCENE_DEV + "Dev_HUD 열기", false, ORDER_SCENE_DEV_HUD)]
        private static void OpenDevHud() { OpenScene(DEV_SYSTEM_SUBDIR + "Dev_HUD"); }

        [MenuItem(MENU_SCENE_DEV + "Dev_WeatherMakerIsolation 열기", false, ORDER_SCENE_DEV_WEATHER)]
        private static void OpenDevWeather() { OpenScene("Dev_WeatherMakerIsolation"); }

        /// <summary> sceneName은 Assets/00.Scenes/ 아래의 상대 경로다. 하위 폴더를 포함할 수 있다 </summary>
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

            // 서버 부트 게이트와 같은 규칙을 쓴다 - 여기서 통과해도 부트에서 걸리면 검사 의미가 없다
            GeumeumBootValidator.ValidateData(
                database.Barrier,
                database.LevelCurve,
                database.Spawn,
                database.Boss,
                GeumeumBootValidator.SceneProfile.Main,
                G_Problems);

            GeumeumBootValidator.ValidatePlayerServices(database.Combat, database.Shop, G_Problems);
            GeumeumBootValidator.ValidateProgressionData(database.Combat, database.Talent,
                database.Score, GeumeumBootValidator.SceneProfile.Main, G_Problems);

            Report("런 데이터 검사", null);
        }

        /// <summary> 현재 씬에 서버를 열 수 있는 필수 구성이 있는지 검사한다. 서버 부트 게이트와 같은 규칙을 쓴다 </summary>
        [MenuItem(MENU_CHECK + "씬 필수 구성 검사", false, ORDER_CHECK_SCENE)]
        private static void CheckSceneSetup()
        {
            G_Problems.Clear();

            // 출시 경로 씬은 전체 구성을 요구하고, 개발 씬은 Dev 프로필로 완화한다
            GeumeumBootValidator.SceneProfile profile =
                GeumeumBootValidator.ResolveSceneProfile(EditorSceneManager.GetActiveScene().name);
            GeumeumBootValidator.ValidateScene(profile, G_Problems);

            var manager = Object.FindAnyObjectByType<GeumeumNetworkManager>(FindObjectsInactive.Include);
            if ((manager != null) && (manager.playerPrefab == null))
            {
                G_Problems.Add("네트워크 매니저에 플레이어 프리팹이 등록되지 않았다");
            }

            Report("씬 필수 구성 검사", null);
        }

        /// <summary>
        /// 로컬 풀 표식이 붙은 프리팹에 네트워크 컴포넌트가 섞여 있는지 검사한다.
        /// 런타임 풀도 거부하지만 그때는 이미 Play 모드라 어느 프리팹이 문제인지 찾기 어렵다
        /// </summary>
        [MenuItem(MENU_CHECK + "풀 대상 프리팹 검사", false, ORDER_CHECK_POOL)]
        private static void CheckPoolPrefabs()
        {
            G_Problems.Clear();

            string[] guids = AssetDatabase.FindAssets(PREFAB_FILTER, new[] { PREFAB_DIR });
            int      count = 0;

            for (int i = 0; i < guids.Length; ++i)
            {
                string     path   = AssetDatabase.GUIDToAssetPath(guids[i]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                if ((prefab == null) || (!prefab.TryGetComponent(out LocalPoolRoot _)))
                {
                    continue;
                }

                ++count;

                // 자식에 붙어 있어도 서버가 수명을 가지는 것은 마찬가지다
                if (prefab.GetComponentInChildren<NetworkIdentity>(true) != null)
                {
                    G_Problems.Add(path + " 에 NetworkIdentity가 있다");
                    continue;
                }

                if (prefab.GetComponentInChildren<NetworkBehaviour>(true) != null)
                {
                    G_Problems.Add(path + " 에 NetworkBehaviour가 있다");
                }
            }

            Debug.Log("[그믐] 풀 대상 프리팹 " + count + "개를 검사했다");
            Report("풀 대상 프리팹 검사", null);
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
                Debug.LogError("[그믐] " + title + " 실패 - " + fatalMessage);
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
