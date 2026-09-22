// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Systems
{
    /// <summary>
    /// 보스 트라이 설정 화면. Dev_BossTrial 씬에 홀로 놓여 레벨, 무기, 장비 옵션, 특성을 직접 정하고
    /// 그 편성 그대로 런을 시작한다. 30분을 걷지 않고 보스 앞 상태를 손으로 만드는 밸런싱 도구다
    /// 값 검증은 서버와 같은 BossTrialRules를 쓰므로 여기서 통과한 편성은 서버에서도 통과한다
    /// </summary>
    public class BossTrialSetupStage : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS

        /****************************************
        *              Constants
        ****************************************/

        private const float PANEL_WIDTH  = 520f;
        private const float PANEL_MARGIN = 16f;
        private const float PANEL_HEIGHT = 720f;
        private const int   OPTION_SLOTS = 2;
        private const int   OPTIONS_PER_ROW = 3;
        private const float REQUEST_RETRY_SECONDS = 0.5f;

        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("시작 버튼을 누르면 이 인원으로 런을 시작한다. 혼자 확인할 때는 1이다")]
        [SerializeField, Min(1)] private int _requiredPlayers = 1;

        private readonly List<int>    _talentNodeIds = new List<int>();
        private readonly List<string> _talentLabels  = new List<string>();
        private readonly List<bool>   _talentPicked  = new List<bool>();

        private readonly EquipmentOptionKind[]  _optionKinds   = new EquipmentOptionKind[OPTION_SLOTS];
        private readonly float[]                _optionValues  = new float[OPTION_SLOTS];
        private readonly string[]               _optionTexts   = new string[OPTION_SLOTS];
        private readonly List<EquipmentOptionKind> _optionCatalog = new List<EquipmentOptionKind>();

        private int        _startLevel    = 10;
        private WeaponKind _weapon        = WeaponKind.Sword;
        private int        _equipmentTier = 3;
        private bool       _useCustom     = true;
        private ItemGrade  _grade         = ItemGrade.Divine;
        private float      _attackPower   = 0f;
        private string     _attackText    = string.Empty;
        private int        _potionCount   = 2;

        private Vector2 _scroll;
        private string  _message = "값을 정하고 아래 시작 버튼을 누른다";
        private bool    _hasSubmitted;

        // 접속 - 무기 선호 - 준비 순서로 진행한다. 선호가 늦으면 로비가 다른 무기로 확정한다
        private bool  _isStarting;
        private float _nextRequestTime;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            for (int slot = 0; slot < OPTION_SLOTS; ++slot)
            {
                _optionTexts[slot] = string.Empty;
            }

            RefreshCatalogs();
        }

        private void Update()
        {
            AdvanceStart();

            // 전실이 편성을 받기 시작하면 화면에서 고른 값을 그대로 낸다
            BossTrialDirector director = BossTrialDirector.LocalInstance;
            if ((!_hasSubmitted) && (director != null) && (director.IsCollecting))
            {
                Submit();
            }
        }

        /// <summary>
        /// 무기는 런이 시작될 때 로비가 확정한다. 접속 직후 자동 준비로 바로 시작해 버리면
        /// 선호가 늦어 다른 무기로 확정되므로, 선호를 보낸 뒤에 준비를 누른다
        /// 로비 씬으로 넘어가면서 앞선 요청이 버려질 수 있어 로스터가 받아들일 때까지 다시 보낸다
        /// </summary>
        private void AdvanceStart()
        {
            if (!_isStarting)
            {
                return;
            }

            RunManager run = RunManager.LocalInstance;
            if ((run != null) && (run.Phase != RunPhase.Lobby))
            {
                _isStarting = false;
                return;
            }

            if ((!NetworkClient.active) || (Time.unscaledTime < _nextRequestTime))
            {
                return;
            }

            LobbyController lobby = FindAnyObjectByType<LobbyController>();
            if (lobby == null)
            {
                return;
            }

            _nextRequestTime = (Time.unscaledTime + REQUEST_RETRY_SECONDS);

            if (!IsPreferenceAccepted())
            {
                lobby.RequestWeaponPreference(_weapon);
                _message = $"{_weapon} 선호를 보냈다";
                return;
            }

            lobby.RequestReady(true);
            _message = $"{_weapon}로 준비했다. 인원이 차면 런이 시작된다";
        }

        /// <summary> 서버 로스터가 내 선호를 받아들였는지. 씬 전환 중에 버려진 요청을 다시 보내려고 본다 </summary>
        private static bool IsPreferenceAccepted()
        {
            LobbyRoster roster = FindAnyObjectByType<LobbyRoster>();
            if (roster == null)
            {
                return false;
            }

            for (int i = 0; i < roster.SlotCount; ++i)
            {
                if (roster.GetSlot(i).HasPreference)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnGUI()
        {
            float height = Mathf.Min(Screen.height - (PANEL_MARGIN * 2f), PANEL_HEIGHT);
            GUILayout.BeginArea(new Rect(PANEL_MARGIN, PANEL_MARGIN, PANEL_WIDTH, height), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("보스 트라이 설정");
            GUILayout.Label(DescribeStatus());

            DrawLevel();
            DrawWeapon();
            DrawEquipment();
            DrawTalents();
            DrawActions();

            GUILayout.Space(6f);
            GUILayout.Label(_message);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void DrawLevel()
        {
            GUILayout.Space(6f);
            GUILayout.Label($"시작 레벨: {_startLevel}");
            _startLevel = Mathf.RoundToInt(GUILayout.HorizontalSlider(_startLevel, 1f,
                Constants.Combat.MAX_LEVEL));

            GUILayout.Label($"물약: {_potionCount}개");
            _potionCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(_potionCount, 0f,
                ResolveMaxPotions()));
        }

        private void DrawWeapon()
        {
            GUILayout.Space(6f);
            GUILayout.Label("무기");

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle((_weapon == WeaponKind.Sword), "검", GUI.skin.button))
            {
                SetWeapon(WeaponKind.Sword);
            }

            if (GUILayout.Toggle((_weapon == WeaponKind.Bow), "활", GUI.skin.button))
            {
                SetWeapon(WeaponKind.Bow);
            }

            if (GUILayout.Toggle((_weapon == WeaponKind.Bell), "방울", GUI.skin.button))
            {
                SetWeapon(WeaponKind.Bell);
            }

            GUILayout.EndHorizontal();
        }

        private void DrawEquipment()
        {
            GUILayout.Space(6f);
            GUILayout.Label($"장비 필드 레벨: {((_equipmentTier < 0) ? "받지 않음" : _equipmentTier.ToString())}");
            _equipmentTier = Mathf.RoundToInt(GUILayout.HorizontalSlider(_equipmentTier, -1f,
                Constants.Combat.FIELD_LEVEL_COUNT - 1));

            if (_equipmentTier < 0)
            {
                return;
            }

            _useCustom = GUILayout.Toggle(_useCustom, "등급과 옵션을 직접 정한다");
            if (!_useCustom)
            {
                GUILayout.Label("서버가 등급 범위에서 굴린다");
                return;
            }

            DrawGrade();

            GUILayout.Label(DescribeAttackRange());
            GUILayout.BeginHorizontal();
            GUILayout.Label("공격력 (비우면 서버가 굴린다)", GUILayout.Width(220f));
            _attackText = GUILayout.TextField(_attackText, GUILayout.Width(80f));
            GUILayout.EndHorizontal();
            _attackPower = ParseFloat(_attackText);

            for (int slot = 0; slot < OPTION_SLOTS; ++slot)
            {
                DrawOptionSlot(slot);
            }
        }

        private void DrawGrade()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle((_grade == ItemGrade.Common), "일반", GUI.skin.button))
            {
                _grade = ItemGrade.Common;
            }

            if (GUILayout.Toggle((_grade == ItemGrade.Talisman), "부적", GUI.skin.button))
            {
                _grade = ItemGrade.Talisman;
            }

            if (GUILayout.Toggle((_grade == ItemGrade.Spiritual), "영험", GUI.skin.button))
            {
                _grade = ItemGrade.Spiritual;
            }

            if (GUILayout.Toggle((_grade == ItemGrade.Divine), "신물", GUI.skin.button))
            {
                _grade = ItemGrade.Divine;
            }

            GUILayout.EndHorizontal();
        }

        private void DrawOptionSlot(int slot)
        {
            GUILayout.Space(4f);
            GUILayout.Label($"옵션 {slot + 1}: {DescribeOption(_optionKinds[slot])}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("없음", GUILayout.Width(70f)))
            {
                _optionKinds[slot] = EquipmentOptionKind.None;
            }

            for (int i = 0; i < _optionCatalog.Count; ++i)
            {
                if (GUILayout.Button(DescribeOption(_optionCatalog[i]), GUILayout.Width(140f)))
                {
                    _optionKinds[slot] = _optionCatalog[i];
                }

                // 한 줄에 셋까지만 두고 넘치면 줄을 다시 연다
                if (((i + 1) % OPTIONS_PER_ROW) != 0)
                {
                    continue;
                }

                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
            }

            GUILayout.EndHorizontal();

            if (_optionKinds[slot] == EquipmentOptionKind.None)
            {
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("값 (비우면 서버가 굴린다)", GUILayout.Width(220f));
            _optionTexts[slot] = GUILayout.TextField(_optionTexts[slot], GUILayout.Width(80f));
            GUILayout.EndHorizontal();
            _optionValues[slot] = ParseFloat(_optionTexts[slot]);
        }

        private void DrawTalents()
        {
            GUILayout.Space(6f);
            GUILayout.Label($"특성 (고른 순서대로 찍는다, 최대 {BossTrialRules.MAX_TALENT_NODES}개)");

            if (_talentNodeIds.Count == 0)
            {
                GUILayout.Label("TalentTreeSO에 노드가 없다");
                return;
            }

            for (int i = 0; i < _talentNodeIds.Count; ++i)
            {
                _talentPicked[i] = GUILayout.Toggle(_talentPicked[i], _talentLabels[i]);
            }
        }

        private void DrawActions()
        {
            GUILayout.Space(8f);
            GUILayout.Label(DescribeBudget());

            if (GUILayout.Button("이 편성으로 시작 (호스트)"))
            {
                StartTrial();
            }

            if (GUILayout.Button("지금 편성 다시 제출"))
            {
                _hasSubmitted = false;
                Submit();
            }

            if (GUILayout.Button("다시 하기 (정산 화면에서)"))
            {
                RequestRetry();
            }

            if (GUILayout.Button("장비, 특성 목록 다시 읽기"))
            {
                RefreshCatalogs();
            }
        }

        private void SetWeapon(WeaponKind weapon)
        {
            if (_weapon == weapon)
            {
                return;
            }

            _weapon = weapon;

            // 무기가 바뀌면 전용 옵션이 통째로 달라진다. 남아 있으면 서버가 거절한다
            for (int slot = 0; slot < OPTION_SLOTS; ++slot)
            {
                _optionKinds[slot] = EquipmentOptionKind.None;
            }

            RefreshCatalogs();
        }

        private void RefreshCatalogs()
        {
            RefreshOptionCatalog();
            RefreshTalentCatalog();
        }

        private void RefreshOptionCatalog()
        {
            _optionCatalog.Clear();

            EquipmentSO equipment = ResolveEquipment();
            if (equipment == null)
            {
                return;
            }

            AppendOptions(equipment.CommonOptions);

            if (equipment.TryGetWeaponOptions(_weapon, out EquipmentSO.OptionEntry[] weaponOptions))
            {
                AppendOptions(weaponOptions);
            }
        }

        private void AppendOptions(EquipmentSO.OptionEntry[] options)
        {
            if (options == null)
            {
                return;
            }

            for (int i = 0; i < options.Length; ++i)
            {
                if (!_optionCatalog.Contains(options[i].Kind))
                {
                    _optionCatalog.Add(options[i].Kind);
                }
            }
        }

        private void RefreshTalentCatalog()
        {
            _talentNodeIds.Clear();
            _talentLabels.Clear();
            _talentPicked.Clear();

            TalentTreeSO tree = (GameDB.IsInitialized) ? GameDB.Talent : null;
            if (tree == null)
            {
                return;
            }

            // 고른 무기의 노드만 보인다. 다른 직업 노드는 어차피 특성 규칙이 거절한다
            for (int i = 0; i < tree.NodeCount; ++i)
            {
                if ((!tree.TryGetNodeAt(i, out TalentTreeSO.NodeEntry node)) ||
                    (node.WeaponKind != _weapon))
                {
                    continue;
                }

                _talentNodeIds.Add(node.NodeId);
                _talentLabels.Add($"{node.NodeId} {node.DisplayName} ({node.PointCost}p)");
                _talentPicked.Add(false);
            }
        }

        private void StartTrial()
        {
            LobbyController.DevRequiredPlayers = _requiredPlayers;
            LobbyController.DevRunMode         = RunMode.BossTrial;

            // 자동 준비를 켜면 무기 선호를 보내기 전에 런이 시작된다. 준비는 아래 순서가 누른다
            LobbyController.DevForceAutoReady = false;

            _hasSubmitted    = false;
            _isStarting      = true;
            _nextRequestTime = 0f;

            if (!NetworkServer.active)
            {
                NetworkManager manager = NetworkManager.singleton;
                if (manager == null)
                {
                    _message = "NetworkManager가 없다. Boot 씬을 거쳐 들어왔는지 확인한다";
                    return;
                }

                manager.StartHost();
                _message = "호스트를 열었다. 준비가 끝나면 런이 시작된다";
                return;
            }

            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (!run.StartRun(RunMode.BossTrial)))
            {
                _message = "지금은 런을 시작할 수 없다. 로비 상태인지 확인한다";
                return;
            }

            _message = "보스 트라이 런을 시작했다";
        }

        private void Submit()
        {
            BossTrialDirector director = BossTrialDirector.LocalInstance;
            if ((director == null) || (!NetworkClient.active))
            {
                _message = "접속한 뒤에 낼 수 있다";
                return;
            }

            BossTrialLoadout       loadout = BuildLoadout();
            BossTrialLoadoutResult local   = BossTrialRules.Validate(ResolveConfig(), in loadout, out int cost);
            if (local != BossTrialLoadoutResult.Success)
            {
                _message = $"보내기 전에 걸렀다: {local}";
                return;
            }

            director.CmdSubmitLoadout(loadout);
            _hasSubmitted = true;
            _message      = $"편성을 냈다. 비용 {cost}";
        }

        private void RequestRetry()
        {
            BossTrialDirector director = BossTrialDirector.LocalInstance;
            if ((director == null) || (!NetworkClient.active))
            {
                _message = "접속한 뒤에 쓸 수 있다";
                return;
            }

            _hasSubmitted = false;
            director.CmdRequestRetry();
            _message = "다시 하기를 요청했다. 로비를 거쳐 새 런으로 시작한다";
        }

        private BossTrialLoadout BuildLoadout()
        {
            return new BossTrialLoadout
            {
                StartLevel         = _startLevel,
                OverrideWeapon     = true,
                Weapon             = _weapon,
                EquipmentTier      = _equipmentTier,
                UseCustomEquipment = _useCustom,
                EquipmentGrade     = _grade,
                AttackPower        = _attackPower,
                Option0Kind        = _optionKinds[0],
                Option0Value       = _optionValues[0],
                Option1Kind        = _optionKinds[1],
                Option1Value       = _optionValues[1],
                PotionCount        = _potionCount,
                TalentNodeIds      = BuildTalentIds(),
            };
        }

        private int[] BuildTalentIds()
        {
            var picked = new List<int>(_talentNodeIds.Count);
            for (int i = 0; i < _talentNodeIds.Count; ++i)
            {
                if (_talentPicked[i])
                {
                    picked.Add(_talentNodeIds[i]);
                }
            }

            return picked.ToArray();
        }

        private string DescribeStatus()
        {
            BossTrialDirector director = BossTrialDirector.LocalInstance;
            RunManager        run      = RunManager.LocalInstance;

            string network = NetworkServer.active
                ? "호스트"
                : (NetworkClient.active ? "클라이언트" : "미접속");
            string phase = (run != null) ? $"{run.Phase} / {run.Mode}" : "런 없음";
            string collect = (director != null)
                ? $"편성 {director.SubmittedCount}/{director.RequiredCount}{(director.IsCollecting ? " 수집 중" : string.Empty)}"
                : "전실 없음";

            return $"{network}   {phase}   {collect}";
        }

        private string DescribeBudget()
        {
            BossTrialLoadout       loadout = BuildLoadout();
            BossTrialLoadoutSO     config  = ResolveConfig();
            BossTrialLoadoutResult result  = BossTrialRules.Validate(config, in loadout, out int cost);
            int points = (config != null) ? config.LoadoutPoints : 0;

            return (result == BossTrialLoadoutResult.Success)
                ? $"비용 {cost} / 예산 {points}"
                : $"낼 수 없다: {result}";
        }

        private string DescribeAttackRange()
        {
            EquipmentSO equipment = ResolveEquipment();
            if ((equipment == null) || (!equipment.TryGetGrade(_grade, out EquipmentSO.GradeEntry entry)))
            {
                return "등급 데이터를 읽지 못했다";
            }

            return $"공격력 범위: {entry.AttackMin:F0} ~ {entry.AttackMax:F0}";
        }

        private static string DescribeOption(EquipmentOptionKind kind)
        {
            return (kind == EquipmentOptionKind.None) ? "없음" : kind.ToString();
        }

        private static float ParseFloat(string text)
        {
            return (float.TryParse(text, out float value) && (value > 0f)) ? value : 0f;
        }

        private static float ResolveMaxPotions()
        {
            BossTrialLoadoutSO config = ResolveConfig();
            return (config != null) ? config.MaxPotions : 0f;
        }

        private static EquipmentSO ResolveEquipment()
        {
            return GameDB.IsInitialized ? GameDB.Equipment : null;
        }

        private static BossTrialLoadoutSO ResolveConfig()
        {
            return GameDB.IsInitialized ? GameDB.BossTrial : null;
        }

#endif
    }
}
