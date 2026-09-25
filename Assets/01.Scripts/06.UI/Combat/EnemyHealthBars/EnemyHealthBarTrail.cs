namespace Geumeum.UI
{
    /// <summary>
    /// 체력 바 한 개의 앞 막대(남은 체력)와 뒤 잔상 막대(방금 깎인 부분)의 순수 상태
    /// 앞 막대는 즉시 새 비율로 간다. 잔상은 깎인 뒤 지연만큼 멈췄다가 초당 비율로 앞 막대까지 따라 내려간다
    /// 비율이 오르면(몰이 강화 등) 두 막대 모두 즉시 맞춘다. 값은 모두 0~1 비율이다
    /// </summary>
    public struct EnemyHealthBarTrail
    {
        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 앞 막대 비율 </summary>
        public float Health;

        /// <summary> 잔상 막대 비율. 항상 Health 이상이다 </summary>
        public float Chip;

        /// <summary> 이 시각 전에는 잔상이 줄지 않는다 </summary>
        public float HoldUntil;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 잔상이 아직 남아 있는지 </summary>
        public bool HasChip => Chip > Health;

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 잔상 없이 한 비율에서 시작한다 </summary>
        public static EnemyHealthBarTrail Create(float ratio)
        {
            float clamped = EnemyHealthBarRules.ClampRatio(ratio);

            return new EnemyHealthBarTrail
            {
                Health    = clamped,
                Chip      = clamped,
                HoldUntil = 0f,
            };
        }

        /// <summary>
        /// 새 체력 비율을 받는다. 줄었으면 잔상을 남기고 지연을 다시 센다
        /// 올랐으면 앞 막대는 즉시 오르고, 잔상보다 높아지면 잔상도 즉시 맞춘다
        /// </summary>
        public void SetRatio(float ratio, float now, float holdSeconds)
        {
            float clamped = EnemyHealthBarRules.ClampRatio(ratio);

            if (clamped < Health)
            {
                HoldUntil = now + ((holdSeconds > 0f) ? holdSeconds : 0f);
            }

            Health = clamped;

            if (Chip < clamped)
            {
                Chip = clamped;
            }
        }

        /// <summary> 지연이 끝났으면 잔상을 초당 drainPerSecond 비율만큼 앞 막대 쪽으로 줄인다. 0 이하면 바로 맞춘다 </summary>
        public void Tick(float now, float deltaSeconds, float drainPerSecond)
        {
            if (Chip <= Health)
            {
                Chip = Health;
                return;
            }

            if (now < HoldUntil)
            {
                return;
            }

            if (drainPerSecond <= 0f)
            {
                Chip = Health;
                return;
            }

            float step = drainPerSecond * ((deltaSeconds > 0f) ? deltaSeconds : 0f);
            float next = Chip - step;

            Chip = (next > Health) ? next : Health;
        }
    }
}
