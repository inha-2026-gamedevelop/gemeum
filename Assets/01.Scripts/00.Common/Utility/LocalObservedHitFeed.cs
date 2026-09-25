// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 동료가 잡귀를 때린 것을 이 화면의 방향 움찔로 나눠 준다(COMBAT_STYLE_DESIGN 8.1, S2)
    /// 네트워크 층(EnemyStatus의 ClientRpc)이 Publish하고, 타격 표현(05.Art ImpactDirector)이 Received를 구독한다
    /// 움찔만 재생한다. 멈춤, 셰이크, 숫자는 공격한 본인의 피해 결과 경로에만 있다
    /// 방향은 수평 yaw 한 바이트로 오간다. 양자화 규칙은 보내는 쪽과 받는 쪽이 같아야 해서 여기 둔다
    /// </summary>
    public static class LocalObservedHitFeed
    {
        /****************************************
        *              Constants
        ****************************************/

        /// <summary> yaw 한 바퀴를 나누는 칸 수. byte 하나에 담는다 </summary>
        public const int YAW_STEPS = 256;

        private const float DEGREES_PER_TURN = 360f;

        /****************************************
        *                Types
        ****************************************/

        /// <summary> 동료 화면에서 재생할 잡귀 피격 하나 </summary>
        public readonly struct ObservedHit
        {
            /// <summary> 맞은 잡귀 netId </summary>
            public readonly uint TargetNetId;

            /// <summary> 가해자 netId. 봇일 수도 있다 </summary>
            public readonly uint AttackerNetId;

            /// <summary> 가해자에서 대상으로 향하는 수평 단위 방향 </summary>
            public readonly Vector3 Direction;

            /// <summary> 움찔 세기를 고르는 단계. 처치(T4)는 오지 않는다 - 대상이 곧바로 사라진다 </summary>
            public readonly ImpactTier Tier;

            public ObservedHit(uint targetNetId, uint attackerNetId, Vector3 direction, ImpactTier tier)
            {
                TargetNetId   = targetNetId;
                AttackerNetId = attackerNetId;
                Direction     = direction;
                Tier          = tier;
            }
        }

        /****************************************
        *              Events
        ****************************************/

        /// <summary> 동료(또는 봇)의 잡귀 피격 하나. 구독자는 판정을 다시 하지 않는다 </summary>
        public static event Action<ObservedHit> Received;

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 받은 피격을 흘려보낸다 </summary>
        public static void Publish(in ObservedHit hit)
        {
            // 구독자 하나가 던져도 나머지 표현은 살아야 한다
            ObserverDispatch.Invoke(Received, hit, null, nameof(LocalObservedHitFeed), nameof(Received));
        }

        /// <summary>
        /// 이 화면이 관찰자로서 재생할 피격인지. 공격한 본인은 피해 결과 경로로 이미 움찔을 재생하므로 false
        /// 내 캐릭터가 아직 없으면(0) 모두 관찰자로 본다
        /// </summary>
        public static bool ShouldObserve(uint attackerNetId, uint localPlayerNetId)
        {
            return (localPlayerNetId == 0u) || (attackerNetId != localPlayerNetId);
        }

        /// <summary> 수평 방향을 yaw 한 바이트로 줄인다. +z가 0이고 +x 쪽으로 늘어난다. 방향이 없거나 비정상이면 false </summary>
        public static bool TryEncodeYaw(Vector3 direction, out byte yaw)
        {
            yaw = 0;

            float sqrMagnitude = (direction.x * direction.x) + (direction.z * direction.z);
            if (float.IsNaN(sqrMagnitude) || float.IsInfinity(sqrMagnitude) || (sqrMagnitude <= Mathf.Epsilon))
            {
                return false;
            }

            float degrees = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            if (degrees < 0f)
            {
                degrees += DEGREES_PER_TURN;
            }

            // 한 바퀴 끝(360도)은 반올림하면 YAW_STEPS가 되므로 0으로 접는다
            int step = Mathf.RoundToInt(degrees * (YAW_STEPS / DEGREES_PER_TURN)) % YAW_STEPS;
            yaw = (byte)step;
            return true;
        }

        /// <summary> yaw 한 바이트를 수평 단위 방향으로 되돌린다 </summary>
        public static Vector3 DecodeYaw(byte yaw)
        {
            float radians = yaw * (DEGREES_PER_TURN / YAW_STEPS) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }

        /// <summary> 도메인 리로드 OFF 대비 정적 상태 초기화 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            Received = null;
        }
    }
}
