// Unity
using UnityEngine;
using UnityEngine.SceneManagement;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Dev;
using Geumeum.AI;
using Mirror;
using Geumeum.Data;

namespace Geumeum.Art
{
    /// <summary>보스 전투 페이즈를 서버에서 재시작한다. 오프라인에서는 배경만 미리 본다</summary>
    public class BossPhaseDebugSwitcher : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS

        /****************************************
        *                Fields
        ****************************************/

        private const string PANEL_ID    = "bossphase";
        private const string PANEL_TITLE = "보스 페이즈";
        private string _message = string.Empty;

        [Header("대상")]
        [SerializeField] private BossArenaPhaseView _phaseView;

        [Header("재생 시작 시 적용할 페이즈")]
        [SerializeField] private BossArenaPhase _startPhase = BossArenaPhase.Shrine;

        // 페이즈 전환 키
        [Header("키")]
        [SerializeField] private KeyCode _shrineKeyCode     = KeyCode.F6;
        [SerializeField] private KeyCode _underworldKeyCode = KeyCode.F7;
        // 토글은 기본으로 끈다. F8은 GDebugOverlay 토글, F3은 런 포기가 이미 쓰고
        // F6/F7이 두 페이즈를 직접 주므로 키를 하나 더 차지할 이유가 없다
        [SerializeField] private KeyCode _toggleKeyCode     = KeyCode.None;


        /****************************************
        *              Unity Event
        ****************************************/

        /// <summary>
        /// 씬에 배치하지 않아도 실행 시작에 스스로 붙는다
        /// 보스 아레나는 런 도중에 로드되므로, 아레나 씬에 심어 두면 그 씬을 직접 열었을 때만 동작한다
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Preload()
        {
            DevToolHost.Attach<BossPhaseDebugSwitcher>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            ResolveView();

            DevPanelHost.Register(PANEL_ID, PANEL_TITLE, Build, Handle);
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;

            DevPanelHost.Unregister(PANEL_ID);
        }

        private void HandleSceneLoaded(Scene loaded, LoadSceneMode mode)
        {
            ResolveView();
        }

        /// <summary>
        /// 아레나 뷰를 찾는다. 아직 없으면 조용히 넘긴다 - 보스 아레나 밖에서는 없는 게 정상이다
        /// Find 계열이라 매 프레임 돌리지 않고 씬이 바뀔 때만 부른다
        /// </summary>
        private void ResolveView()
        {
            if (_phaseView != null)
            {
                return;
            }

            _phaseView = FindAnyObjectByType<BossArenaPhaseView>();
            if (_phaseView == null)
            {
                return;
            }

            if (!NetworkClient.active && !NetworkServer.active) { _phaseView.ShowPhase(_startPhase); }
        }

        private void Update()
        {
            if (Input.GetKeyDown(_shrineKeyCode))
            {
                ApplyPhase(1);
                return;
            }

            if (Input.GetKeyDown(_underworldKeyCode))
            {
                ApplyPhase(2);
                return;
            }

            if (!Input.GetKeyDown(_toggleKeyCode))
            {
                return;
            }

            bool isShrine = (_phaseView == null || _phaseView.CurrentPhase == BossArenaPhase.Shrine);
            ApplyPhase(isShrine ? 2 : 1);
        }

        /// <summary>
        /// 페이즈 전환은 브라우저 개발 패널에서 누른다
        /// 화면 왼쪽 위 고정 라벨은 다른 개발 표시와 같은 자리를 써서 서로를 덮었다
        /// </summary>
        private void Build(DevPanelBuilder builder)
        {
            builder.Label("호스트: 실제 전투 페이즈 재시작 / 오프라인: 배경 미리보기");
            builder.Label("전투 재시작 시 HP·부위·원혼을 초기화하며 해당 페이즈 시작 HP를 적용한다.");
            var source = BossPhaseSourceLocator.Current;
            if (source != null) { builder.Label($"전투 페이즈: {source.PhaseSnapshot.PhaseIndex} / {source.PhaseSnapshot.Stage}"); }
            builder.Button("shrine", $"1페이즈 재시작 ({_shrineKeyCode})");
            builder.Button("underworld", $"2페이즈 재시작 ({_underworldKeyCode})");
            builder.Button("groggy", "그로기 / 꼬리 노출");
            if (source != null && GameDB.IsInitialized &&
                GameDB.Boss.TryGetPhase(source.PhaseSnapshot.PhaseIndex, out var phase) && phase.PatternIds != null)
            {
                builder.Header("현재 페이즈 패턴 실행");
                foreach (int id in phase.PatternIds)
                {
                    if (GameDB.Boss.TryGetPattern(id, out var pattern))
                    { builder.Button("pattern." + id, pattern.DisplayName); }
                }
            }
            builder.Label(_message);
        }

        private void Handle(DevCommand command)
        {
            if (command.Control == "groggy" || command.Control.StartsWith("pattern.", System.StringComparison.Ordinal))
            {
                if (!NetworkServer.active || !DevAccess.IsUnlocked) { _message = "개발 도구가 열린 호스트 패널에서 실행한다."; return; }
                BossController boss = ResolveServerBoss();
                bool applied = boss != null && (command.Control == "groggy" ? boss.ServerDevEnterGroggy() :
                    int.TryParse(command.Control.Substring(8), out int pattern) && boss.ServerDevBeginPattern(pattern));
                _message = applied ? "보스 테스트 실행 완료" : "보스 / 대상 / 현재 전투 단계 조건을 확인한다.";
                return;
            }
            if (command.Control == "shrine")
            {
                ApplyPhase(1);
                return;
            }

            if (command.Control == "underworld")
            {
                ApplyPhase(2);
            }
        }

        private void ApplyPhase(int phase)
        {
            if (NetworkServer.active)
            {
                // 보스는 씬 로드 이후 네트워크 스폰된다. 버튼을 누를 때 현재 인스턴스를 찾는다
                BossController boss = ResolveServerBoss();
                if (boss == null) { _message = "서버에 스폰된 보스가 없다. 보스 전투 진입을 확인한다."; return; }
                _message = boss.ServerDevRestartPhase(phase, out string failure)
                    ? $"{phase}페이즈 서버 적용 완료 (보스 #{boss.netId})" : "페이즈 재시작 실패: " + failure;
                return;
            }
            if (NetworkClient.active) { _message = "실제 페이즈 변경은 호스트 패널에서 실행한다."; return; }
            ResolveView();
            if (_phaseView == null) { _message = "보스 아레나가 없다."; return; }
            _phaseView.ShowPhase((BossArenaPhase)phase);
            _message = $"{phase}페이즈 배경 미리보기 (오프라인)";
        }

        private static BossController ResolveServerBoss()
        {
            // 씬 미리보기나 스폰 해제 중인 오브젝트를 찾아 성공으로 오인하지 않는다.
            foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
            {
                if (identity != null && identity.isServer && identity.gameObject.activeInHierarchy &&
                    identity.TryGetComponent(out BossController boss)) { return boss; }
            }
            return null;
        }

#endif
    }
}
