// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Art
{
    /// <summary> 결과 주도 타격 표현이 고르는 셰이크 종류. 실제 모양은 CameraConfigSO의 ShakeSpec이 정한다 </summary>
    public enum ImpactShakeKind : byte
    {
        None       = 0,
        HitLight   = 1, // ResolveHitShake(HitStrength.Light)
        SwordSkill = 2, // ShakeSwordSkill
        BowStab    = 3, // ShakeBowStab
        Critical   = 4, // ShakeCritical
        EchoBurst  = 5, // ShakeEchoBurst
    }

    /// <summary>
    /// 타격 표현의 순수 규칙(COMBAT_STYLE_DESIGN 5, 5.1, S1, S2, S5). 수치는 받지 않고 넘겨받은 설정값만 조합한다
    /// MonoBehaviour와 분리해 EditMode 테스트가 그대로 부른다
    /// </summary>
    public static class ImpactRules
    {
        /****************************************
        *              Constants
        ****************************************/

        /// <summary> 먹 번짐 오버레이를 재생하는 받아베기 최소 표식 수(S6 "표식 3개 받아베기만"). 설계 규칙이지 밸런스 수치가 아니다 </summary>
        public const byte COUNTER_INK_MARKS = 3;

        /****************************************
        *              Public API
        ****************************************/

        /// <summary>
        /// 서버 시각(NetworkTime.time) 기준으로 너무 늦게 도착한 결과인지. 시각이 망가진 결과도 늦은 것으로 본다
        /// now는 이 화면의 NetworkTime.time, sent는 서버가 결과를 만들 때 실은 NetworkTime.time이다
        /// </summary>
        public static bool IsLate(double nowServerTime, double sentServerTime, float discardSeconds)
        {
            if (double.IsNaN(sentServerTime) || double.IsInfinity(sentServerTime) ||
                double.IsNaN(nowServerTime) || double.IsInfinity(nowServerTime))
            {
                return true;
            }

            // 클라이언트의 NetworkTime.time은 스냅샷 보간 버퍼만큼 서버보다 뒤에 있다.
            // 그래서 결과가 이 시계보다 조금 앞서 오는 것은 정상이고, 잰 지연은 실제보다 버퍼만큼 짧다(폐기가 그만큼 너그럽다)
            return (nowServerTime - sentServerTime) > discardSeconds;
        }

        /// <summary> 피해 결과가 늦었는지. 결과의 ServerTime으로 잰다 - GameplayTime은 멈추는 구간이 있고 복제값도 같이 늦게 온다 </summary>
        public static bool IsLateResult(in DamageResult result, double nowServerTime, float discardSeconds)
        {
            return IsLate(nowServerTime, result.ServerTime, discardSeconds);
        }

        /// <summary> 피해 원인으로 무기를 고른다. 원인만으로 모르면 지금 든 무기를 쓴다 </summary>
        public static WeaponKind ResolveWeapon(DamageSourceKind source, WeaponKind fallback)
        {
            switch (source)
            {
                case DamageSourceKind.Melee:
                case DamageSourceKind.MeleeFinisher:
                case DamageSourceKind.SwordSkill:
                case DamageSourceKind.CounterSlash:
                    return WeaponKind.Sword;

                case DamageSourceKind.Arrow:
                case DamageSourceKind.PrecisionShot:
                case DamageSourceKind.BowStab:
                case DamageSourceKind.WedgeBurst:
                    return WeaponKind.Bow;

                case DamageSourceKind.Bell:
                case DamageSourceKind.EchoBurst:
                case DamageSourceKind.BellField:
                    return WeaponKind.Bell;

                default:
                    return fallback;
            }
        }

        /// <summary>
        /// 내 캐릭터 멈춤 길이(S5). 활 원거리는 손이 멈추면 조준이 끊기므로 별도 배수를 쓰고,
        /// R 찌르기는 근접 예외라 기존 찌르기 멈춤 값을 그대로 쓴다
        /// </summary>
        public static float ResolveAttackerHitStop(DamageSourceKind source, WeaponKind weapon, float tierSeconds,
            float swordScale, float bowRangedScale, float bellScale, float bowStabSeconds)
        {
            if (source == DamageSourceKind.BowStab)
            {
                return Mathf.Max(0f, bowStabSeconds);
            }

            float scale;
            switch (weapon)
            {
                case WeaponKind.Bow:
                    scale = bowRangedScale;
                    break;

                case WeaponKind.Bell:
                    scale = bellScale;
                    break;

                default:
                    scale = swordScale;
                    break;
            }

            return Mathf.Max(0f, tierSeconds * Mathf.Max(0f, scale));
        }

        /// <summary> 단계와 원인으로 셰이크 종류를 고른다. T5는 공동 순간 경로가 따로 흔든다 </summary>
        public static ImpactShakeKind ResolveShake(ImpactTier tier, DamageSourceKind source)
        {
            switch (tier)
            {
                case ImpactTier.Light:
                    return ImpactShakeKind.HitLight;

                case ImpactTier.Medium:
                    return (source == DamageSourceKind.BowStab) ? ImpactShakeKind.BowStab : ImpactShakeKind.SwordSkill;

                case ImpactTier.Heavy:
                    // 치명, 표식 2~3개 받아베기, 정밀 사격 명중은 치명 흔들림을 같이 쓴다
                    return (source == DamageSourceKind.EchoBurst) ? ImpactShakeKind.EchoBurst : ImpactShakeKind.Critical;

                case ImpactTier.Kill:
                    return ImpactShakeKind.Critical;

                default:
                    return ImpactShakeKind.None;
            }
        }

        /// <summary> 대상이 멈추면 판독이 망가지는 상태인지. 예고, 돌진, 공격은 모션 시각이 판정과 어긋나 보이고 넉백은 미끄러진다 </summary>
        public static bool IsEnemyBusy(EnemyVisualState state)
        {
            return (state == EnemyVisualState.Telegraph) ||
                   (state == EnemyVisualState.Charge) ||
                   (state == EnemyVisualState.Attack) ||
                   (state == EnemyVisualState.Knockback);
        }

        /// <summary> 움찔 세기(0~1). 맞는 순간 가장 크게 젖히고 길이 동안 부드럽게 돌아온다 </summary>
        public static float FlinchWeight(float elapsedSeconds, float durationSeconds)
        {
            if ((durationSeconds <= 0f) || (elapsedSeconds < 0f) || (elapsedSeconds >= durationSeconds))
            {
                return 0f;
            }

            float remain = 1f - (elapsedSeconds / durationSeconds);
            return remain * remain;
        }

        /// <summary> 맞은 방향으로 상체를 젖히는 월드 회전축. 방향을 모르면 zero </summary>
        public static Vector3 FlinchAxis(Vector3 hitDirection)
        {
            Vector3 flat = new Vector3(hitDirection.x, 0f, hitDirection.z);
            if (flat.sqrMagnitude <= Mathf.Epsilon)
            {
                return Vector3.zero;
            }

            // 위쪽 벡터를 hitDirection 쪽으로 기울이는 축이다
            return Vector3.Cross(Vector3.up, flat.normalized);
        }

        /// <summary> 화면 가장자리 플래시 세기(0~1). 켜지는 순간 가장 밝다 </summary>
        public static float FlashWeight(float elapsedSeconds, float durationSeconds)
        {
            if ((durationSeconds <= 0f) || (elapsedSeconds < 0f) || (elapsedSeconds >= durationSeconds))
            {
                return 0f;
            }

            return 1f - (elapsedSeconds / durationSeconds);
        }

        /// <summary> 먹 번짐 세기(0~1). 가장자리에서 번져 길이의 한가운데 가장 짙고 끝에 빠진다(S6) </summary>
        public static float InkWeight(float elapsedSeconds, float durationSeconds)
        {
            if ((durationSeconds <= 0f) || (elapsedSeconds < 0f) || (elapsedSeconds >= durationSeconds))
            {
                return 0f;
            }

            return Mathf.Sin(Mathf.PI * (elapsedSeconds / durationSeconds));
        }

        /// <summary>
        /// 받아베기 먹 번짐을 재생할 결과인지(S6). 표식 3개 이상 받아베기만, 한 공격 실행에 한 번
        /// 실행 번호를 모르는(0) 결과는 실행으로 묶을 수 없어 재생 중 중복 방지에만 맡긴다
        /// </summary>
        public static bool ShouldPlayCounterInk(DamageSourceKind source, byte detail, uint attackerNetId,
            uint executionId, uint lastAttackerNetId, uint lastExecutionId)
        {
            if ((source != DamageSourceKind.CounterSlash) || (detail < COUNTER_INK_MARKS))
            {
                return false;
            }

            if (executionId == 0u)
            {
                return true;
            }

            return (executionId != lastExecutionId) || (attackerNetId != lastAttackerNetId);
        }
    }
}
