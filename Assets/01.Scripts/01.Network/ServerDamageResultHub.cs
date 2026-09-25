// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Network
{
    /// <summary>
    /// 서버 피해 결과의 단일 수집 창구(COMBAT_STYLE_DESIGN 8.1, S3, S10)
    /// 잡귀·보스 본체·꼬리·원혼이 HP를 실제로 깎은 직후 한 번 보고하고, 공격자의 PlayerAttack이 프레임 끝에 꺼내 소유 클라이언트에게만 보낸다
    /// 봇처럼 연결이 없는 공격자의 결과는 만들지 않는다. 판정을 하지 않으며 보고를 빠뜨려도 피해 자체는 바뀌지 않는다
    /// 동료 화면의 잡귀 방향 움찔(관찰자 전송)도 여기서 대상별 간격을 거른다. 이쪽은 봇이 때린 것도 보낸다
    /// </summary>
    public static class ServerDamageResultHub
    {
        /****************************************
        *              Constants
        ****************************************/

        /// <summary> TargetRpc 한 번에 담는 최대 건수. 넘으면 다음 프레임에 이어서 보낸다 </summary>
        public const int MAX_RESULTS_PER_SEND = 32;

        /****************************************
        *                Fields
        ****************************************/

        private static DamageResultQueue   _queue       = new DamageResultQueue();
        private static ObservedHitThrottle _observedHits = new ObservedHitThrottle();

        /****************************************
        *              Public API
        ****************************************/

        /// <summary>
        /// 대상 HP가 실제로 appliedAmount만큼 줄었음을 보고한다. 서버에서만 호출한다
        /// targetCenter는 적중점이 없을 때 쓰는 대상(부위) 중심이며 방향 계산의 끝점이다
        /// </summary>
        public static void ServerReport(in DamageInfo damage, uint targetNetId, sbyte partIndex,
            bool targetIsBoss, float appliedAmount, bool isKill, Vector3 targetCenter)
        {
            if ((!NetworkServer.active) || (damage.AttackerNetId == DamageInfo.NO_ATTACKER) ||
                (appliedAmount <= Constants.Combat.DAMAGE_EPSILON))
            {
                return;
            }

            // 결과는 공격자 소유 화면만 받는다. 연결이 없는 봇이나 이미 사라진 공격자의 결과는 받을 사람이 없다
            if ((!NetworkServer.spawned.TryGetValue(damage.AttackerNetId, out NetworkIdentity attacker)) ||
                (attacker == null) || (attacker.connectionToClient == null))
            {
                return;
            }

            _queue.TryEnqueue(damage, targetNetId, partIndex, targetIsBoss, appliedAmount, isKill,
                targetCenter, attacker.transform.position,
                RunTimeLocator.Current.GameplayElapsedSeconds, NetworkTime.time, out _);
        }

        /// <summary>
        /// 잡귀 피격을 동료 화면의 방향 움찔로 보낼지 정한다. 서버에서만 호출한다
        /// 대상별로 움찔 반복 방지 간격(FlinchRepeatGuardSeconds)에 한 번만 통과시키고, 보낼 방향(yaw 한 바이트)과 단계를 돌려준다
        /// 처치 피해는 넘기지 않는다 - 대상이 곧바로 파괴되어 움찔할 몸이 없다. 보스는 움찔 대상이 아니다
        /// </summary>
        public static bool ServerTryAcceptObservedHit(in DamageInfo damage, uint targetNetId, Vector3 targetCenter,
            out byte yaw, out ImpactTier tier)
        {
            yaw  = 0;
            tier = ImpactTier.Whiff;

            if ((!NetworkServer.active) || (!GameDB.IsInitialized) ||
                (damage.AttackerNetId == DamageInfo.NO_ATTACKER))
            {
                return false;
            }

            // 봇도 공격자로 스폰되어 있으므로 연결 여부는 보지 않는다
            if ((!NetworkServer.spawned.TryGetValue(damage.AttackerNetId, out NetworkIdentity attacker)) ||
                (attacker == null))
            {
                return false;
            }

            Vector3 direction = DamageResultQueue.ResolveHitDirection(attacker.transform.position, targetCenter);
            if (!LocalObservedHitFeed.TryEncodeYaw(direction, out yaw))
            {
                return false;
            }

            // 방향이 없는 피격이 간격을 차지하지 않도록 거르기는 마지막에 한다
            if (!_observedHits.TryAccept(targetNetId, NetworkTime.time, GameDB.Presentation.FlinchRepeatGuardSeconds))
            {
                return false;
            }

            DamageResult probe = new DamageResult
            {
                Source     = damage.Source,
                Detail     = damage.Detail,
                IsCritical = damage.IsCritical,
            };

            tier = ImpactTierResolver.Resolve(probe);
            return true;
        }

        /// <summary> 잡귀가 서버에서 사라졌다. 관찰자 전송 간격 기록을 지운다 </summary>
        public static void ServerForgetObservedTarget(uint targetNetId)
        {
            _observedHits.Forget(targetNetId);
        }

        /// <summary> 참가자 신원만 아는 경로(원혼 반격 등)에서 공격자 netId를 찾는다 </summary>
        public static bool TryResolveAttackerNetId(ParticipantId participant, out uint attackerNetId)
        {
            attackerNetId = DamageInfo.NO_ATTACKER;
            if ((!participant.IsValid) ||
                (!ParticipantDirectory.TryGet(participant, out NetworkIdentity actor)) || (actor == null))
            {
                return false;
            }

            attackerNetId = actor.netId;
            return (attackerNetId != DamageInfo.NO_ATTACKER);
        }

        /// <summary> 한 공격자의 결과를 buffer 크기만큼 꺼낸다 </summary>
        public static int Drain(uint attackerNetId, DamageResult[] buffer, out int droppedSinceLastDrain)
        {
            return _queue.Drain(attackerNetId, buffer, out droppedSinceLastDrain);
        }

        /// <summary> 한 공격자의 남은 결과 수 </summary>
        public static int GetPendingCount(uint attackerNetId)
        {
            return _queue.GetPendingCount(attackerNetId);
        }

        /// <summary> 한 공격자의 대기열을 버린다. 연결 해제·씬 전환·런 변경 때 PlayerAttack이 부른다 </summary>
        public static void ClearAttacker(uint attackerNetId)
        {
            _queue.ClearAttacker(attackerNetId);
        }

        /// <summary> 모든 대기열을 버린다 </summary>
        public static void ClearAll()
        {
            _queue.Clear();
            _observedHits.Clear();
        }

        /// <summary> 도메인 리로드 OFF 대비 정적 상태 초기화. 사건 번호도 처음부터 다시 센다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            _queue        = new DamageResultQueue();
            _observedHits = new ObservedHitThrottle();
        }
    }
}
