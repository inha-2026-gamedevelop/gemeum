// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Network
{
    /// <summary>
    /// 서버 부트 게이트와 에디터 검사 메뉴가 함께 쓰는 데이터·씬 구성 검사 규칙.
    /// 두 곳이 각자 다른 항목을 검사하면 에디터 검사를 통과한 씬이 실제 부트에서는 걸러지지 않은 채 열릴 수 있다
    /// </summary>
    public static class GeumeumBootValidator
    {
        /// <summary> 씬 필수 구성 검사 범위. Dev 씬은 로비·정산 없이 전투만 켜는 경우가 있어 완화된 프로필을 쓴다 </summary>
        public enum SceneProfile
        {
            Main,     // 실제 플레이 씬, 로비부터 결과까지 전체 경로가 있어야 한다
            DevScene, // 전투 확인용 Dev 씬, 네트워크·이벤트·틱 스케줄러만 있으면 된다
        }

        private const string EDITOR_ONLY_TAG = "EditorOnly";

        /****************************************
        *                 Data
        ****************************************/

        /// <summary> 런 상태 머신·레벨 곡선·스폰 구성이 서버를 열 수 있는 값인지 검사한다 </summary>
        public static void ValidateData(BarrierPhaseSO barrier, LevelCurveSO levelCurve, SpawnConfigSO spawn, List<string> failures)
        {
            ValidateBarrier(barrier, failures);
            ValidateLevelCurve(levelCurve, failures);
            ValidateSpawn(spawn, failures);
        }

        private static void ValidateBarrier(BarrierPhaseSO barrier, List<string> failures)
        {
            if (barrier == null)
            {
                failures.Add("결계 단계 데이터가 없다");
                return;
            }

            RunPhaseTimeline timeline = RunTimelineBuilder.Build(barrier);
            if (!timeline.Validate(barrier.RunTimeLimitSeconds, out string error))
            {
                failures.Add("결계 단계 데이터가 런 상태 머신 계약을 어긴다 — " + error);
            }
        }

        private static void ValidateLevelCurve(LevelCurveSO levelCurve, List<string> failures)
        {
            if (levelCurve == null)
            {
                failures.Add("레벨 곡선 데이터가 없다");
                return;
            }

            // PlayerStatus가 시작 HP를 여기서 읽는다
            if (!levelCurve.TryGetLevel(Constants.Combat.START_LEVEL, out LevelCurveSO.LevelEntry entry))
            {
                failures.Add("레벨 곡선에 시작 레벨 " + Constants.Combat.START_LEVEL + " 항목이 없다");
                return;
            }

            if (entry.MaxHealth <= 0f)
            {
                failures.Add("시작 레벨의 최대 HP가 0 이하다");
            }
        }

        private static void ValidateSpawn(SpawnConfigSO spawn, List<string> failures)
        {
            if (spawn == null)
            {
                failures.Add("스폰 데이터가 없다");
                return;
            }

            // Shrink3와 Boss는 스폰이 없는 것이 정상이라 검사 대상이 아니다
            RunPhase[] spawning = { RunPhase.Farming, RunPhase.Shrink1, RunPhase.Shrink2 };
            for (int i = 0; i < spawning.Length; ++i)
            {
                if (!spawn.TryGetPhase(spawning[i], out SpawnConfigSO.PhaseEntry entry))
                {
                    failures.Add("스폰 데이터에 " + spawning[i] + " 구간이 없다");
                    continue;
                }

                if (entry.ConcurrentMax <= 0)
                {
                    failures.Add(spawning[i] + " 구간의 동시 존재 수가 0이다");
                }
            }
        }

        /****************************************
        *                 Scene
        ****************************************/

        /// <summary> 현재 씬에 프로필이 요구하는 필수 컴포넌트가 있는지 검사한다 </summary>
        public static void ValidateScene(SceneProfile profile, List<string> failures)
        {
            RequireInScene<GeumeumNetworkManager>("네트워크 매니저", failures);
            RequireInScene<GameDBBootstrap>("GameDB 부트스트랩", failures);
            RequireInScene<GameEventBus>("이벤트 버스", failures);
            RequireInScene<AuthorityTickScheduler>("권위 틱 스케줄러", failures);

            if (profile != SceneProfile.Main)
            {
                return;
            }

            RequireInScene<LobbyController>("로비 컨트롤러", failures);
            RequireInScene<RunManager>("런 매니저", failures);
            RequireInScene<FrameRecorder>("프레임 기록기", failures);
        }

        // 씬 오브젝트에 NetworkIdentity가 있으면 Mirror가 비활성으로 두므로 비활성도 포함해 찾는다
        private static void RequireInScene<T>(string label, List<string> failures) where T : Component
        {
            var found = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
            if (found == null)
            {
                failures.Add(label + "(" + typeof(T).Name + ")가 씬에 없다");
                return;
            }

            if (TryFindEditorOnlyAncestor(found.transform, out string ancestor))
            {
                failures.Add(label + "가 EditorOnly인 " + ancestor + " 아래에 있다, 빌드에서 통째로 제거된다");
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
    }
}
