using UnityEngine;
using TMPro;
using Geumeum.Common;
using Geumeum.Combat;
using Geumeum.Data;

namespace Geumeum.UI
{
    /// <summary> 소유자에게만 무기별 조작과 현재 잔향/쐐기 상태를 보여 준다 </summary>
    public sealed class WeaponAbilityHUD : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;
        private PlayerStatus _status;
        private PlayerWeapon _weapon;
        private PlayerAttack _attack;
        private SupportBuffController _buff;
        private ICombatLoadoutProvider _loadout;
        private float _nextRefresh;
        private string _lastText;

        private void Update()
        {
            if (_label == null || Time.unscaledTime < _nextRefresh) { return; }
            _nextRefresh = Time.unscaledTime + 0.1f;
            if (_status != PlayerStatus.Local)
            {
                _status = PlayerStatus.Local;
                _weapon = _status != null ? _status.GetComponent<PlayerWeapon>() : null;
                _attack = _status != null ? _status.GetComponent<PlayerAttack>() : null;
                _buff = _status != null ? _status.GetComponent<SupportBuffController>() : null;
                _loadout = _status != null ? _status.GetComponent<ICombatLoadoutProvider>() : null;
            }
            string text = string.Empty;
            if (_status != null && !_status.IsDead && !_status.IsSwallowed && _weapon != null &&
                _weapon.HasWeapon && _attack != null && GameDB.IsInitialized)
            {
                double now = RunTimeLocator.Current.GameplayElapsedSeconds;
                if (_weapon.Weapon == WeaponKind.Bow)
                {
                    WeaponAbilityMark wedge = _attack.Wedge;
                    text = wedge.IsActive(now)
                        ? $"쐐기 {wedge.Count}/{GameDB.Combat.WedgeHits} - {Mathf.CeilToInt((float)(wedge.ExpiresAt - now))}초\n일반 2회 / 정밀 1회로 약점 파열"
                        : "R 슬라이딩 긁기: 쐐기 설치\n좌클릭: 화살 발사";
                }
                else if (_weapon.Weapon == WeaponKind.Sword)
                {
                    int marks = _attack.CounterMarks;
                    text = $"반격 {new string('*', marks)}{new string('.', 3 - marks)} - 우클릭 받아치기\n" +
                        (marks > 0 ? "R 표식 반격 - 기본 쿨다운과 독립" : "R 기본 횡베기 - 패링 성공으로 표식 획득");
                }
                else if (_weapon.Weapon == WeaponKind.Bell)
                {
                    WeaponAbilityMark a = _attack.EchoFirst, b = _attack.EchoSecond;
                    int first = a.IsActive(now) ? a.Count : 0, second = b.IsActive(now) ? b.Count : 0;
                    float cost = GameDB.Combat.EchoSpiritCost;
                    if (_loadout != null && _loadout.TryBuildLoadout(_status.Participant, out CombatStatsSnapshot snapshot) && snapshot.IsValid)
                    { cost = snapshot.ResolveSpiritCost(cost, snapshot.EchoSpiritCostReduction + _attack.EchoDiscountRemaining); }
                    string state = _attack.IsRecalling ? "회수 중" : _attack.EchoCooldownRemaining > 0
                        ? $"{_attack.EchoCooldownRemaining:F1}초" : first + second == 0 ? "잔향 없음"
                        : _status.Spirit < cost ? "신력 부족" : "Q 되울림";
                    text = $"{state} - 잔향 {first}/3  {second}/3\n신력 {cost:0} 소비 - 사용 후 {Mathf.Max(0, _status.Spirit - cost):0} | 우클릭 치유";
                    // 진의 남은 시간과 내가 받는 버프의 남은 시간은 따로 읽어야 한다
                    // 둘을 더한 값은 어느 쪽으로도 보장되지 않는다
                    if (_attack.BellFieldEnd > now)
                    { text = $"내 공명진 {Mathf.Max(0f, (float)(_attack.BellFieldEnd - now)):F1}초\n" + text; }
                }
                string buffLine = DescribeResonance(now);
                if (!string.IsNullOrEmpty(buffLine))
                { text = string.IsNullOrEmpty(text) ? buffLine : buffLine + "\n" + text; }
                string name = _weapon.Weapon == WeaponKind.Sword ? "받아베기" : _weapon.Weapon == WeaponKind.Bow ? "파고들기" : "공명진";
                float skillCost = GameDB.Combat.Skill(_weapon.Weapon).Cost;
                if (_loadout != null && _loadout.TryBuildLoadout(_status.Participant, out CombatStatsSnapshot skillStats) && skillStats.IsValid)
                {
                    skillCost = _weapon.Weapon == WeaponKind.Bell
                        ? skillStats.ResolveSpiritCost(skillCost, skillStats.SkillSpiritCostReduction)
                        : skillStats.ResolveStaminaCost(skillCost);
                }

                float resource = _weapon.Weapon == WeaponKind.Bell ? _status.Spirit : _status.Stamina;
                string skillState = _attack.IsPlacingField
                    ? (_attack.IsPlacementPending ? "설치 확인 중"
                        : _attack.IsPlacementValid ? "좌클릭 설치 - 우클릭/Esc 취소" : "설치할 수 없는 위치")
                    : _attack.IsWeaponSkillActive ? "사용 중" : _attack.WeaponSkillCooldownRemaining > 0 ?
                    $"{_attack.WeaponSkillCooldownRemaining:F1}초" : resource < skillCost ? "자원 부족" : "준비";
                text = $"R {name} - {skillState} - {(_weapon.Weapon == WeaponKind.Bell ? "신력" : "스태미나")} {skillCost:0}" +
                    (string.IsNullOrEmpty(text) ? "" : "\n" + text);
            }
            if (_lastText != text) { _label.text = text; _lastText = text; }
        }

        /// <summary> 내가 지금 받고 있는 공명진 버프. 실제 서버 효과량과 남은 시간을 그대로 쓴다 </summary>
        private string DescribeResonance(double now)
        {
            if (_buff == null || _buff.ActiveDamageMultiplier <= 1f) { return string.Empty; }
            int percent = Mathf.RoundToInt((_buff.ActiveDamageMultiplier - 1f) * 100f);
            if (_buff.IsRefreshing(now, GameDB.Combat.BellFieldSampleInterval))
            { return $"공명진 공격력 +{percent}% - 영역 내 갱신 중"; }
            float remaining = Mathf.Max(0f, (float)(_buff.ActiveExpiryGameplayTime - now));
            return $"공명진 공격력 +{percent}% - 잔여 {remaining:F1}초";
        }
    }
}
