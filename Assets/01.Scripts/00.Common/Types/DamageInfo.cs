namespace Geumeum.Common
{
    /// <summary>
    /// 피해 한 건의 내용. 가해자를 netId로만 들고 있어 파트 간 구체 클래스 참조가 생기지 않는다.
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

        /// <summary> 타격감 강도, 아트가 표현을 정한다 </summary>
        public HitStrength Strength { get; }

        /****************************************
        *                Methods
        ****************************************/

        public DamageInfo(float amount, uint attackerNetId, HitStrength strength)
        {
            Amount        = amount;
            AttackerNetId = attackerNetId;
            Strength      = strength;
        }

        /// <summary> 결계, 환경처럼 가해자가 없는 피해 </summary>
        public static DamageInfo FromEnvironment(float amount)
        {
            return new DamageInfo(amount, NO_ATTACKER, HitStrength.Light);
        }
    }
}
