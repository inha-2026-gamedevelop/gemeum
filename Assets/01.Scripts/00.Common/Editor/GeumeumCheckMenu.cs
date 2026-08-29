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

        private const string SCENE_DIR       = "Assets/00.Scenes/";
        private const string SCENE_EXT       = ".unity";
        private const string GAME_DB_PATH    = "Assets/09.Data/Resources/GameDB.asset";
        private const string EDITOR_ONLY_TAG = "EditorOnly";

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

            CheckBarrier(database.Barrier);
            CheckLevelCurve(database.LevelCurve);
            CheckSpawn(database.Spawn);

            Report("런 데이터 검사", null);
        }

        private static void CheckBarrier(BarrierPhaseSO barrier)
        {
            if (barrier == null)
            {
                G_Problems.Add("결계 단계 데이터가 없다");
                return;
            }

            RunPhaseTimeline timeline = RunTimelineBuilder.Build(barrier);
            if (!timeline.Validate(barrier.RunTimeLimitSeconds, out string error))
            {
                G_Problems.Add("결계 단계 데이터 — " + error);
            }
        }

        private static void CheckLevelCurve(LevelCurveSO levelCurve)
        {
            if (levelCurve == null)
            {
                G_Problems.Add("레벨 곡선 데이터가 없다");
                return;
            }

            // PlayerStatus가 시작 HP를 여기서 읽는다
            if (!levelCurve.TryGetLevel(Constants.Combat.START_LEVEL, out LevelCurveSO.LevelEntry entry))
            {
                G_Problems.Add("레벨 곡선에 시작 레벨 " + Constants.Combat.START_LEVEL + " 항목이 없다");
                return;
            }

            if (entry.MaxHealth <= 0f)
            {
                G_Problems.Add("시작 레벨의 최대 HP가 0 이하다");
            }
        }

        private static void CheckSpawn(SpawnConfigSO spawn)
        {
            if (spawn == null)
            {
                G_Problems.Add("스폰 데이터가 없다");
                return;
            }

            // Shrink3와 Boss는 스폰이 없는 것이 정상이라 검사 대상이 아니다
            RunPhase[] spawning = { RunPhase.Farming, RunPhase.Shrink1, RunPhase.Shrink2 };
            for (int i = 0; i < spawning.Length; ++i)
            {
                if (!spawn.TryGetPhase(spawning[i], out SpawnConfigSO.PhaseEntry entry))
                {
                    G_Problems.Add("스폰 데이터에 " + spawning[i] + " 구간이 없다");
                    continue;
                }

                if (entry.ConcurrentMax <= 0)
                {
                    G_Problems.Add(spawning[i] + " 구간의 동시 존재 수가 0이다");
                }
            }
        }

        /// <summary> 현재 씬에 서버를 열 수 있는 필수 구성이 있는지 검사한다 </summary>
        [MenuItem(MENU_CHECK + "씬 필수 구성 검사", false, ORDER_CHECK_SCENE)]
        private static void CheckSceneSetup()
        {
            G_Problems.Clear();

            RequireInScene<GeumeumNetworkManager>("네트워크 매니저");
            RequireInScene<GameDBBootstrap>("GameDB 부트스트랩");
            RequireInScene<GameEventBus>("이벤트 버스");
            RequireInScene<LobbyController>("로비 컨트롤러");
            RequireInScene<RunManager>("런 매니저");
            RequireInScene<FrameRecorder>("프레임 기록기");

            var manager = Object.FindAnyObjectByType<GeumeumNetworkManager>(FindObjectsInactive.Include);
            if ((manager != null) && (manager.playerPrefab == null))
            {
                G_Problems.Add("네트워크 매니저에 플레이어 프리팹이 등록되지 않았다");
            }

            Report("씬 필수 구성 검사", null);
        }

        // 씬 오브젝트에 NetworkIdentity가 있으면 Mirror가 비활성으로 두므로 비활성도 포함해 찾는다
        private static void RequireInScene<T>(string label) where T : Component
        {
            var found = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
            if (found == null)
            {
                G_Problems.Add(label + "(" + typeof(T).Name + ")가 씬에 없다");
                return;
            }

            if (TryFindEditorOnlyAncestor(found.transform, out string ancestor))
            {
                G_Problems.Add(label + "가 EditorOnly인 " + ancestor + " 아래에 있다, 빌드에서 통째로 제거된다");
            }
        }

        // EditorOnly는 자식까지 빌드에서 제외된다, 에디터에서는 정상 동작해 발견이 늦다
        private static bool TryFindEditorOnlyAncestor(Transform target, out string ancestorName)
        {
            for (Transform t = target; t != null; t = t.parent)
            {
                if (t.gameObject.CompareTag(EDITOR_ONLY_TAG))
                {
                    ancestorName = t.name;
                    return true;
                }
            }

            ancestorName = null;
            return false;
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
