// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Dev;
using Geumeum.Network;

namespace Geumeum.Systems
{
    /// <summary>
    /// 보스 트라이 임시 조작 패널. 정식 편성 UI가 나오기 전까지 전실 흐름을 혼자 돌려 보기 위한 개발 도구다
    /// 예산 계산은 서버와 같은 BossTrialRules를 쓰므로, 여기서 통과한 편성은 서버에서도 통과한다
    /// </summary>
    public class BossTrialTestPanel : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS

        /****************************************
        *                Preload
        ****************************************/

        /// <summary> 씬에 배치하지 않아도 실행 시작에 스스로 붙는다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Preload()
        {
            DevToolHost.Attach<BossTrialTestPanel>();
        }

        /****************************************
        *              Constants
        ****************************************/

        private const string PANEL_ID    = "bosstrial";
        private const string PANEL_TITLE = "보스 트라이";

        private const string NO_EQUIPMENT_OPTION = "장비 없음";

        /****************************************
        *                Fields
        ****************************************/

        private string _message = string.Empty;

        // 아직 서버에 내지 않은 화면 위의 선택이다
        private int _equipmentTier = -1;
        private int _potionCount   = 0;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            DevPanelHost.Register(PANEL_ID, PANEL_TITLE, Build, Handle);
        }

        private void OnDestroy()
        {
            DevPanelHost.Unregister(PANEL_ID);
        }

        /****************************************
        *                Panel
        ****************************************/

        private void Build(DevPanelBuilder builder)
        {
            BuildStatus(builder);

            builder.Header("다음 런");
            builder.Toggle("trial", "다음 런을 보스 트라이로 (호스트만)", IsTrialRequested());
            builder.Button("unlock", "해금 기록 저장");

            builder.Label("편성 제출·재시도는 인게임 편성 / 정산 UI에서 조작한다.");

            if (!string.IsNullOrEmpty(_message))
            {
                builder.Label(_message);
            }
        }

        private static void BuildStatus(DevPanelBuilder builder)
        {
            builder.Label($"해금: {(BossTrialUnlock.IsUnlocked ? "열림" : "잠김")}   " +
                $"실행 인자: {(BossTrialUnlock.HasCommandLineOverride() ? "있음" : "없음")}");

            BossTrialDirector director = BossTrialDirector.LocalInstance;
            if (director == null)
            {
                builder.Label("전실 컴포넌트를 아직 못 찾았다. 접속 후에 보인다");
                return;
            }

            builder.Label($"편성 모으는 중: {(director.IsCollecting ? "예" : "아니오")}   " +
                $"제출: {director.SubmittedCount}/{director.RequiredCount}");

            RunManager run = RunManager.LocalInstance;
            if (run != null)
            {
                builder.Label($"단계: {run.Phase}   모드: {run.Mode}");
            }

            BossTrialLoadoutSO config = ResolveConfig();
            builder.Label((config == null)
                ? "BossTrialLoadoutSO가 GameDB에 없다. 편성이 전부 거절된다"
                : $"예산: {config.LoadoutPoints}   시작 레벨: {config.StartLevel}   물약 상한: {config.MaxPotions}");
        }

        private void Handle(DevCommand command)
        {
            switch (command.Control)
            {
                case "trial":
                    SetTrialMode(command.IsOn);
                    return;

                case "unlock":
                    BossTrialUnlock.MarkRunStarted();
                    _message = "해금 기록을 저장했다";
                    return;

                case "tier":
                    SetEquipmentTier(command);
                    return;

                case "potion":
                    SetPotionCount(command);
                    return;

                case "submit":
                    Submit(_equipmentTier, _potionCount);
                    return;

                case "submitempty":
                    Submit(-1, 0);
                    return;

                case "retry":
                    RequestRetry();
                    return;

                default:
                    return;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void SetTrialMode(bool enabled)
        {
            if (!TryGetDirector(out BossTrialDirector director))
            {
                return;
            }

            director.CmdSetTrialMode(enabled);
            _message = enabled ? "다음 런을 보스 트라이로 요청했다" : "다음 런을 정식 런으로 되돌렸다";
        }

        private void SetEquipmentTier(in DevCommand command)
        {
            if (!command.TryGetInt(out int index))
            {
                return;
            }

            // 0번 항목이 장비 없음이라 나머지가 곧 필드 레벨이다
            _equipmentTier = (index - 1);
            _message       = $"장비를 {DescribeTier()}으로 골랐다";
        }

        private void SetPotionCount(in DevCommand command)
        {
            if (!command.TryGetInt(out int index))
            {
                return;
            }

            _potionCount = index;
            _message     = $"물약을 {_potionCount}개로 골랐다";
        }

        private void Submit(int tier, int potions)
        {
            if (!TryGetDirector(out BossTrialDirector director))
            {
                return;
            }

            var loadout = new BossTrialLoadout
            {
                EquipmentTier = tier,
                PotionCount   = potions,
            };

            BossTrialLoadoutResult local = BossTrialRules.Validate(ResolveConfig(), in loadout, out int cost);
            if (local != BossTrialLoadoutResult.Success)
            {
                _message = $"보내기 전에 걸렀다: {local}";
                return;
            }

            director.CmdSubmitLoadout(loadout);
            _message = $"편성을 냈다. 비용 {cost}";
        }

        private void RequestRetry()
        {
            if (!TryGetDirector(out BossTrialDirector director))
            {
                return;
            }

            director.CmdRequestRetry();
            _message = "다시 하기를 요청했다. 로비를 거쳐 새 런으로 시작한다";
        }

        private bool TryGetDirector(out BossTrialDirector director)
        {
            director = BossTrialDirector.LocalInstance;
            if ((director == null) || (!NetworkClient.active))
            {
                _message = "접속한 뒤에 쓸 수 있다";
                return false;
            }

            return true;
        }

        private static bool IsTrialRequested()
        {
            BossTrialDirector director = BossTrialDirector.LocalInstance;
            return ((director != null) && (director.IsTrialRequested));
        }

        private string DescribeTier()
        {
            return (_equipmentTier < 0) ? NO_EQUIPMENT_OPTION : _equipmentTier.ToString();
        }

        private string DescribeBudget()
        {
            var loadout = new BossTrialLoadout
            {
                EquipmentTier = _equipmentTier,
                PotionCount   = _potionCount,
            };

            BossTrialLoadoutSO     config = ResolveConfig();
            BossTrialLoadoutResult result = BossTrialRules.Validate(config, in loadout, out int cost);
            int points = (config != null) ? config.LoadoutPoints : 0;

            return (result == BossTrialLoadoutResult.Success)
                ? $"이 편성의 비용: {cost} / {points}"
                : $"이 편성은 낼 수 없다: {result}";
        }

        private static BossTrialLoadoutSO ResolveConfig()
        {
            return GameDB.IsInitialized ? GameDB.BossTrial : null;
        }

#endif
    }
}
