// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 피해 한 건의 내용. 가해자를 netId로만 들고 있어 파트 간 구체 클래스 참조가 생기지 않는다
    /// 서버가 만들어 IDamageable에 넘기며 클라이언트는 이 값을 만들지 않는다
    /// </summary>
    public readonly struct DamageInfo
    {
        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 가해자가 없는 피해에 쓰는 netId, Mirror는 0을 유효한 netId로 발급하지 않는다 </summary>
        public const uint NO_ATTACKER = 0u;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 최종 피해량, 배수와 방어 보정이 모두 끝난 값 </summary>
        public float Amount { get; }

        /// <summary> 가해자 netId, 결계와 환경 피해처럼 귀속 대상이 없으면 NO_ATTACKER </summary>
        public uint AttackerNetId { get; }

        /// <summary> 이탈 뒤 명중해도 기여를 잃지 않는 가해 참가자 신원 </summary>
        public ParticipantId AttackerParticipant { get; }

        /// <summary> 타격감 강도, 아트가 표현을 정한다 </summary>
        public HitStrength Strength { get; }

        /// <summary> 피해를 만든 행동 종류. 피해 결과의 표현 단계에만 쓰고 판정에는 쓰지 않는다 </summary>
        public DamageSourceKind Source { get; }

        /// <summary> 종류별 보조값. 받아베기는 소비한 표식 수, 잔향은 단계 </summary>
        public byte Detail { get; }

        /// <summary> 가해 행동의 실행 번호. 한 공격이 여러 대상을 맞힌 것을 묶는다. 모르면 0 </summary>
        public uint ExecutionId { get; }

        /// <summary> 서버가 굴린 치명타인가 </summary>
        public bool IsCritical { get; }

        /// <summary> 판정에 쓴 적중 위치가 있는가. 없으면 결과 허브가 대상 중심을 쓴다 </summary>
        public bool HasHitPoint { get; }

        /// <summary> 판정에 쓴 적중 위치(월드). HasHitPoint가 false면 의미가 없다 </summary>
        public Vector3 HitPoint { get; }

        /****************************************
        *                Methods
        ****************************************/

        public DamageInfo(float amount, uint attackerNetId, HitStrength strength)
            : this(amount, attackerNetId, ParticipantId.None, strength)
        {
        }

        public DamageInfo(float amount, uint attackerNetId, ParticipantId attackerParticipant,
            HitStrength strength)
            : this(amount, attackerNetId, attackerParticipant, strength,
                DamageSourceKind.Unknown, 0, 0u, false, false, Vector3.zero)
        {
        }

        private DamageInfo(float amount, uint attackerNetId, ParticipantId attackerParticipant,
            HitStrength strength, DamageSourceKind source, byte detail, uint executionId,
            bool isCritical, bool hasHitPoint, Vector3 hitPoint)
        {
            Amount              = amount;
            AttackerNetId       = attackerNetId;
            AttackerParticipant = attackerParticipant;
            Strength            = strength;
            Source              = source;
            Detail              = detail;
            ExecutionId         = executionId;
            IsCritical          = isCritical;
            HasHitPoint         = hasHitPoint;
            HitPoint            = hitPoint;
        }

        /// <summary> 결계, 환경처럼 가해자가 없는 피해 </summary>
        public static DamageInfo FromEnvironment(float amount)
        {
            return new DamageInfo(amount, NO_ATTACKER, ParticipantId.None, HitStrength.Light);
        }

        /// <summary> 피해 결과 표현에 쓰는 행동 정보를 붙인 복사본. 피해량과 가해자는 그대로다 </summary>
        public DamageInfo WithSource(DamageSourceKind source, uint executionId, byte detail = 0,
            bool isCritical = false)
        {
            return new DamageInfo(Amount, AttackerNetId, AttackerParticipant, Strength,
                source, detail, executionId, isCritical, HasHitPoint, HitPoint);
        }

        /// <summary> 판정에 쓴 적중 위치를 붙인 복사본. 유한하지 않은 위치는 붙이지 않는다 </summary>
        public DamageInfo WithHitPoint(Vector3 hitPoint)
        {
            if ((float.IsNaN(hitPoint.x)) || (float.IsInfinity(hitPoint.x)) ||
                (float.IsNaN(hitPoint.y)) || (float.IsInfinity(hitPoint.y)) ||
                (float.IsNaN(hitPoint.z)) || (float.IsInfinity(hitPoint.z)))
            {
                return this;
            }

            return new DamageInfo(Amount, AttackerNetId, AttackerParticipant, Strength,
                Source, Detail, ExecutionId, IsCritical, true, hitPoint);
        }
    }
}
