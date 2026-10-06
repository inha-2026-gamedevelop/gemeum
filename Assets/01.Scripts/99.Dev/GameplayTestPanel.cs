using System;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Combat;
using Geumeum.Network;
using Geumeum.Systems;

namespace Geumeum.Dev
{
    /// <summary>정상 HUD로 시험할 조건을 준비하는 서버 전용 개발 도구</summary>
    public sealed class GameplayTestPanel : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS
        private const string PANEL_ID = "gameplay";
        private static readonly string[] Grades = { "일반", "부적", "영험", "신물" };
        private static readonly string[] Weapons = { "검", "활", "방울" };
        private readonly List<InventoryController> _targets = new List<InventoryController>();
        private readonly List<EquipmentSO.OptionEntry> _options = new List<EquipmentSO.OptionEntry>();
        private uint _targetId;
        private ParticipantId _targetParticipant;
        private RunId _targetRun;
        private int _grade;
        private int _weapon;
        private int _option;
        private int _optionSlot;
        private int _amount = 1;
        private int _experience = 100;
        private int _currency = 500;
        private int _seconds = 60;
        private float _roll = 0.5f;
        private string _message = "호스트 패널에서 대상의 테스트 조건을 준비한다.";
        private GrowthManager _growth;
        private CurrencyManager _money;
        private DropDirector _drops;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Preload() { DevToolHost.Attach<GameplayTestPanel>(); }
        private void Awake() { DevPanelHost.Register(PANEL_ID, "성장 · 장비 · 지급", Build, Handle); }
        private void OnDestroy() { DevPanelHost.Unregister(PANEL_ID); }

        private void Build(DevPanelBuilder b)
        {
            b.Label(_message);
            if (!NetworkServer.active) { b.Label("게임 상태 변경은 호스트의 개발 패널에서 실행한다."); return; }
            if (!GameDB.IsInitialized) { b.Label("게임 데이터를 기다리는 중"); return; }
            RefreshTargets();
            var names = new string[_targets.Count];
            string selected = string.Empty;
            for (int i = 0; i < _targets.Count; ++i)
            {
                names[i] = DescribeTarget(_targets[i]);
                if (_targets[i].netId == _targetId) { selected = names[i]; }
            }
            b.Choice("target", "지급 / 장착 변경 대상 (봇 포함)", selected, names);
            b.Header("파티 성장 · 공용 재화");
            if (_growth != null) { b.Label($"Lv.{_growth.Level} · 누적 경험치 {_growth.Experience} · 특성 포인트 {_growth.State.TalentPoints}"); }
            b.Text("experience", "지급 경험치", _experience.ToString());
            b.Button("xp", "경험치 지급");
            b.Button("levelup", "레벨 +1 / 특성 포인트");
            b.Button("maxlevel", "최대 레벨 / 특성 포인트");
            b.Text("currency", "지급 노잣돈", _currency.ToString());
            b.Button("money", "노잣돈 지급");
            b.Label("특성 투자·초기화는 인게임 특성 창에서 시험한다.");
            b.Header("진행 · 전투 조건");
            b.Text("seconds", "전진할 게임 시간 (초)", _seconds.ToString());
            b.Button("time", "시간 전진 / 결계·객주·시간 초과");
            b.Button("hurt", "대상 최대 HP의 절반 피해");
            b.Button("heal", "대상 HP 회복");
            b.Button("spirit", "대상 신력 충전");
            b.Button("defeat", "필드 잡귀 처치 / 드롭·보상");
            b.Label("피해·회복은 실제 판정 규칙을 따른다. 피해 시험 전 무한 자원을 끈다.");

            b.Header("장비 지급");
            b.Choice("weapon", "무기", Weapons[_weapon], Weapons);
            b.Choice("grade", "지급 / 변환 등급", Grades[_grade], Grades);
            b.Button("grant", "선택 장비를 배낭에 지급");
            b.Label("장착·해제·양도는 인게임 배낭에서 시험한다.");

            b.Header("현재 착용 장비 강제 조정 (티켓 소비 없음)");
            InventoryController target = ResolveTarget();
            if (target != null && target.HasEquippedWeapon)
            {
                WeaponCombatSnapshot item = target.EquippedWeapon;
                b.Label($"#{item.InstanceId} {item.WeaponKind} / {item.Grade} / 공격력 {item.AttackPower:0.##}");
                b.Label($"옵션 1: {item.Option0Kind} {item.Option0Value:0.###}\n옵션 2: {item.Option1Kind} {item.Option1Value:0.###}");
                RefreshOptions(item.WeaponKind);
                var optionNames = new string[_options.Count + 1];
                optionNames[0] = "없음";
                for (int i = 0; i < _options.Count; ++i) { optionNames[i + 1] = _options[i].Kind.ToString(); }
                _option = Mathf.Clamp(_option, 0, _options.Count);
                b.Choice("option", "장비 특수옵션", optionNames[_option], optionNames);
                b.Choice("slot", "옵션 슬롯", _optionSlot == 0 ? "1" : "2", "1", "2");
                b.Slider("roll", "등급 / 옵션 범위 내 수치 (%)", _roll * 100f, 0f, 100f);
                b.Button("reroll", "현재 장비 무작위 재조정");
                b.Button("convert", "선택 등급으로 변환");
                b.Button("attack", "공격력 수치 적용");
                b.Button("applyoption", "선택 특수옵션 적용");
                b.Label("기본 무기(#0)는 조정할 수 없다. 장비를 지급하고 HUD에서 장착한다.");
            }

            b.Header("소모품 지급");
            b.Text("amount", "수량 (소지 / 중첩 제한 적용)", _amount.ToString());
            if (GameDB.Shop != null && GameDB.Shop.Items != null)
            {
                foreach (var item in GameDB.Shop.Items)
                {
                    if (item.Kind != ShopItemKind.None) { b.Button("supply." + (int)item.Kind, item.DisplayName + " 지급"); }
                }
            }
            b.Label("물약·티켓 사용, 구매는 HUD에서 시험한다. 티켓 재조정과 개발용 강제 조정은 별개다.");
        }

        private void RefreshTargets()
        {
            RunId runId = RunManager.ServerInstance != null ? RunManager.ServerInstance.RunId : default;
            if (runId != _targetRun)
            {
                _targetRun = runId;
                _targetParticipant = ParticipantId.None;
                _targetId = 0u;
            }
            _targets.Clear();
            foreach (PlayerStatus player in PlayerStatus.ServerPlayers)
            {
                if (player != null && player.TryGetComponent(out InventoryController inventory) &&
                    NetworkServer.spawned.TryGetValue(inventory.netId, out NetworkIdentity identity) &&
                    identity == inventory.netIdentity && identity.isServer && identity.gameObject.activeInHierarchy)
                { _targets.Add(inventory); }
            }
            _targets.Sort((a, b) =>
            {
                ParticipantId left = a.GetComponent<PlayerStatus>().Participant;
                ParticipantId right = b.GetComponent<PlayerStatus>().Participant;
                int order = left.CompareTo(right);
                return order != 0 ? order : a.netId.CompareTo(b.netId);
            });
            // 씬 전환은 플레이어를 새 netId로 스폰한다. 선택한 사람/봇의 신원으로 다시 연결한다.
            if (_targetParticipant.IsValid)
            {
                _targetId = 0u;
                foreach (InventoryController inventory in _targets)
                {
                    if (inventory.TryGetComponent(out PlayerStatus status) && status.Participant == _targetParticipant)
                    { _targetId = inventory.netId; return; }
                }
                return;
            }
            foreach (InventoryController inventory in _targets)
            {
                if (inventory.netId == _targetId) { SelectTarget(inventory); return; }
            }
            if (_targets.Count > 0) { SelectTarget(_targets[0]); }
        }

        private void SelectTarget(InventoryController inventory)
        {
            _targetId = inventory.netId;
            _targetParticipant = inventory.TryGetComponent(out PlayerStatus status)
                ? status.Participant : ParticipantId.None;
            _option = 0;
        }

        private InventoryController ResolveTarget()
        {
            return NetworkServer.spawned.TryGetValue(_targetId, out NetworkIdentity identity) && identity != null
                ? identity.GetComponent<InventoryController>() : null;
        }

        private string DescribeTarget(InventoryController target)
        {
            string name = ResolveTargetName(target);
            int duplicateNames = 0;
            foreach (InventoryController inventory in _targets)
            {
                if (ResolveTargetName(inventory) == name) { ++duplicateNames; }
            }
            if (duplicateNames > 1) { name += $" (파티 {_targets.IndexOf(target) + 1})"; }
            if (target.TryGetComponent(out PlayerWeapon weapon) && weapon.HasWeapon)
            { name += " · " + Weapons[(int)weapon.Weapon]; }
            return name;
        }

        private static string ResolveTargetName(InventoryController target)
        {
            if (target.TryGetComponent(out IDisplayNamed named) && !string.IsNullOrWhiteSpace(named.DisplayName))
            { return named.DisplayName.Trim(); }
            NetworkConnectionToClient connection = target.connectionToClient;
            if (connection != null)
            {
                if (connection.identity != null && connection.identity.TryGetComponent(out IDisplayNamed owner) &&
                    !string.IsNullOrWhiteSpace(owner.DisplayName)) { return owner.DisplayName.Trim(); }
                foreach (NetworkIdentity owned in connection.owned)
                {
                    if (owned != null && owned.TryGetComponent(out IDisplayNamed ownerName) &&
                        !string.IsNullOrWhiteSpace(ownerName.DisplayName)) { return ownerName.DisplayName.Trim(); }
                }
            }
            return target.TryGetComponent(out PlayerStatus status) && status.Participant.Kind == ParticipantKind.Bot
                ? "봇" : "이름 확인 중";
        }

        private void RefreshOptions(WeaponKind weapon)
        {
            _options.Clear();
            if (GameDB.Equipment.CommonOptions != null) { _options.AddRange(GameDB.Equipment.CommonOptions); }
            if (GameDB.Equipment.TryGetWeaponOptions(weapon, out var options) && options != null) { _options.AddRange(options); }
        }

        private void Handle(DevCommand command)
        {
            if (!NetworkServer.active || !DevAccess.IsUnlocked || !GameDB.IsInitialized)
            { _message = "개발 도구가 열린 호스트에서만 실행할 수 있다."; return; }
            int value;
            switch (command.Control)
            {
                case "target":
                    if (command.TryGetInt(out value) && value >= 0 && value < _targets.Count)
                    {
                        InventoryController selected = _targets[value];
                        if (selected != null && NetworkServer.spawned.TryGetValue(selected.netId, out var identity) &&
                            identity == selected.netIdentity) { SelectTarget(selected); }
                        else { _message = "대상이 사라졌다. 대상 플레이어를 다시 선택한다."; }
                    }
                    return;
                case "grade": if (command.TryGetInt(out value) && value >= 0 && value < Grades.Length) { _grade = value; } return;
                case "weapon": if (command.TryGetInt(out value) && value >= 0 && value < Weapons.Length) { _weapon = value; } return;
                case "slot": if (command.TryGetInt(out value) && value >= 0 && value < 2) { _optionSlot = value; } return;
                case "option": if (command.TryGetInt(out value) && value >= 0 && value <= _options.Count) { _option = value; } return;
                case "amount": SetPositive(command, ref _amount); return;
                case "experience": SetPositive(command, ref _experience); return;
                case "currency": SetPositive(command, ref _currency); return;
                case "seconds": SetPositive(command, ref _seconds); return;
                case "roll":
                    if (command.TryGetFloat(out float percent) && !float.IsNaN(percent) && !float.IsInfinity(percent))
                    { _roll = Mathf.Clamp01(percent / 100f); }
                    return;
            }
            RunManager run = RunManager.ServerInstance;
            if (run == null || !run.RunId.IsValid || run.IsPaused || RunSceneDirector.IsServerTransitioning ||
                run.Phase == RunPhase.Lobby || run.Phase == RunPhase.Result)
            { _message = "진행 중인 런에서만 실행할 수 있다. 일시정지 / 씬 전환을 끝낸 뒤 재시도한다."; return; }
            RefreshTargets();
            // 실제 명령 시점에만 찾는다. 아직 스폰되지 않은 서비스를 매 스냅샷에서 탐색하지 않는다
            if (_growth == null) { _growth = FindAnyObjectByType<GrowthManager>(); }
            if (_money == null) { _money = FindAnyObjectByType<CurrencyManager>(); }
            if (_drops == null) { _drops = FindAnyObjectByType<DropDirector>(); }
            switch (command.Control)
            {
                case "xp": Result(_growth != null && _growth.ServerTryAddExperience(_experience), "경험치 지급"); return;
                case "levelup":
                    Result(_growth != null && _growth.Level < GameDB.LevelCurve.MaxLevel &&
                        _growth.ServerTrySetPartyLevel(_growth.Level + 1), "레벨업"); return;
                case "maxlevel": Result(_growth != null && _growth.ServerTrySetPartyLevel(GameDB.LevelCurve.MaxLevel), "최대 레벨"); return;
                case "money": Result(_money != null && _money.TryCredit(_currency), "노잣돈 지급"); return;
                case "time": Result(run.ServerDevAdvanceTime(_seconds), "시간 전진"); return;
                case "defeat":
                    int defeated = 0;
                    foreach (var enemy in FindObjectsByType<Geumeum.AI.EnemyStatus>(FindObjectsInactive.Exclude))
                    {
                        if (enemy.IsDead) { continue; }
                        enemy.TakeDamage(DamageInfo.FromEnvironment(enemy.MaxHealth));
                        if (enemy.IsDead) { ++defeated; }
                    }
                    _message = $"잡귀 {defeated}마리 처치. 드롭은 기획 확률을 따른다.";
                    return;
            }
            InventoryController target = ResolveTarget();
            if (target == null) { _message = "대상이 사라졌다. 대상 플레이어를 다시 선택한다."; return; }
            PlayerStatus status = target.GetComponent<PlayerStatus>();
            if (status != null)
            {
                if (command.Control == "hurt") { status.TakeDamage(DamageInfo.FromEnvironment(status.MaxHealth * 0.5f)); _message = $"피해 적용 후 HP {status.Health:0.##}"; return; }
                if (command.Control == "heal") { Result(status.TryRestoreHealth(status.MaxHealth), "HP 회복"); return; }
                if (command.Control == "spirit") { status.ServerAddSpirit(GameDB.Combat.SpiritMax); _message = $"신력 충전 후 {status.Spirit:0.##}"; return; }
            }
            if (command.Control.StartsWith("supply.", StringComparison.Ordinal))
            {
                if (int.TryParse(command.Control.Substring(7), out value) &&
                    GameDB.Shop.TryGetItem((ShopItemKind)value, out var item))
                { InventoryResultMessage(target.ServerTryStoreConsumable(item.Kind, _amount, item.CarryLimit, item.MaxStack, target.Revision), target, "소모품 지급"); }
                else { _message = "소모품 지급 실패: 상품 정보가 없다."; }
                return;
            }
            if (command.Control == "grant")
            {
                if (_drops == null || !_drops.ServerTryGenerateEquipment((WeaponKind)_weapon, 0, out var item))
                { _message = "장비 생성기가 준비되지 않았다."; return; }
                item.Grade = (ItemGrade)_grade;
                item = Generator().Reroll(item);
                InventoryResultMessage(target.ServerTryStoreEquipment(item, target.Revision), target, "장비 지급");
                return;
            }
            WeaponCombatSnapshot current = target.EquippedWeapon;
            if (!target.HasEquippedWeapon || !current.HasEquipment) { _message = "HUD에서 지급 장비를 먼저 착용한다."; return; }
            switch (command.Control)
            {
                case "reroll": current = Generator().Reroll(current); break;
                case "convert": current.Grade = (ItemGrade)_grade; current = Generator().Reroll(current); break;
                case "attack":
                    if (!GameDB.Equipment.TryGetGrade(current.Grade, out var grade)) { return; }
                    current.AttackPower = Mathf.Lerp(grade.AttackMin, grade.AttackMax, _roll);
                    break;
                case "applyoption":
                    RefreshOptions(current.WeaponKind);
                    if (_option < 0 || _option > _options.Count) { _message = "옵션을 다시 선택한다."; return; }
                    var option = _option == 0 ? default : _options[_option - 1];
                    if (_optionSlot == 1 && option.Kind != EquipmentOptionKind.None && current.Option0Kind == EquipmentOptionKind.None)
                    { _message = "첫 번째 옵션을 먼저 지정한다."; return; }
                    if (option.Kind != EquipmentOptionKind.None &&
                        ((_optionSlot == 0 ? current.Option1Kind : current.Option0Kind) == option.Kind))
                    { _message = "같은 옵션은 두 슬롯에 중복 적용할 수 없다."; return; }
                    if (_optionSlot == 0) { current.Option0Kind = option.Kind; current.Option0Value = Mathf.Lerp(option.ValueMin, option.ValueMax, _roll); }
                    else { current.Option1Kind = option.Kind; current.Option1Value = Mathf.Lerp(option.ValueMin, option.ValueMax, _roll); }
                    if (current.Option0Kind == EquipmentOptionKind.None)
                    { current.Option0Kind = current.Option1Kind; current.Option0Value = current.Option1Value; current.Option1Kind = EquipmentOptionKind.None; current.Option1Value = 0f; }
                    break;
                default: return;
            }
            _message = "착용 장비 변경: " + target.ServerDevReplaceEquipped(current);
        }

        private static EquipmentGenerator Generator()
        {
            // ID는 DropDirector에서만 발급한다. 여기서는 같은 ID의 수치만 다시 굴린다
            return new EquipmentGenerator(GameDB.Equipment, GameDB.DropTable, new System.Random());
        }
        private void SetPositive(DevCommand command, ref int destination)
        {
            if (command.TryGetInt(out int value) && value > 0) { destination = value; }
            else { _message = "양의 정수를 입력한다."; }
        }
        private void Result(bool success, string action) { _message = action + (success ? ": 성공" : ": 적용 실패 (현재 상태 / 한도 확인)"); }
        private void InventoryResultMessage(InventoryResult result, InventoryController target, string action)
        {
            _message = $"{action} · {DescribeTarget(target)}: " + (result == InventoryResult.Success ? "성공" : $"실패 ({result})");
        }
#endif
    }
}
