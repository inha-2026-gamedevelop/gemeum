namespace Geumeum.Art
{
    /// <summary>
    /// 대상 하나의 멈춤 상한과 재발동 유예(COMBAT_STYLE_DESIGN 5.1, S1)
    /// 첫 요청부터 최대 길이를 넘지 않고, 끝난 뒤 유예 동안은 새 요청을 받지 않는다. 시계는 호출부가 넘긴다
    /// </summary>
    public struct TargetHitStopLimiter
    {
        /****************************************
        *                Fields
        ****************************************/

        private double _startTime;
        private double _endTime;
        private double _cooldownUntil;

        /****************************************
        *              Properties
        ****************************************/

        public bool   IsActive { get; private set; }
        public double EndTime  => _endTime;

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 멈춤을 요청한다. 새로 멈추기 시작했으면 true - 호출부가 이때 원래 속도를 저장한다 </summary>
        public bool Request(double now, float seconds, float maxSeconds)
        {
            if ((seconds <= 0f) || (maxSeconds <= 0f) || float.IsNaN(seconds) || float.IsInfinity(seconds))
            {
                return false;
            }

            if (IsActive)
            {
                // 같은 대상의 종료 시각을 무한히 늘리지 않는다
                double capped = System.Math.Min(_startTime + maxSeconds, now + seconds);
                if (capped > _endTime)
                {
                    _endTime = capped;
                }

                return false;
            }

            if (now < _cooldownUntil)
            {
                return false;
            }

            IsActive   = true;
            _startTime = now;
            _endTime   = now + System.Math.Min(seconds, maxSeconds);
            return true;
        }

        /// <summary> 시간이 다 됐으면 끝내고 true - 호출부가 이때 속도를 복원한다 </summary>
        public bool Tick(double now, float cooldownSeconds)
        {
            if (!IsActive || (now < _endTime))
            {
                return false;
            }

            IsActive       = false;
            _cooldownUntil = _endTime + System.Math.Max(0f, cooldownSeconds);
            return true;
        }

        /// <summary> 우선 상태(예고, 사망 등)로 바뀌어 지금 끊는다. 멈춰 있었으면 true </summary>
        public bool Cancel(double now, float cooldownSeconds)
        {
            if (!IsActive)
            {
                return false;
            }

            IsActive       = false;
            _cooldownUntil = now + System.Math.Max(0f, cooldownSeconds);
            return true;
        }
    }
}
