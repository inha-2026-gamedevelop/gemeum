// System
using System;

namespace Geumeum.Common
{
    /// <summary>
    /// 런 하나의 신원. 서버가 런을 열 때마다 새로 발급하고 세션 안에서 재사용하지 않는다.
    ///
    /// 같은 연결로 여러 판을 이어서 하므로, 이전 런에서 늦게 도착한 요청과 이벤트가
    /// 다음 판에 섞여 들어올 수 있다. 런 전용 요청과 이벤트에 이 값을 실어 두면
    /// 받는 쪽이 "지금 런의 것인가"를 한 번에 가를 수 있다
    /// </summary>
    public readonly struct RunId : IEquatable<RunId>
    {
        /****************************************
        *                Fields
        ****************************************/

        public static readonly RunId None = new RunId(0u);

        public readonly uint Value;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsValid => (Value != 0u);

        /****************************************
        *              Constructors
        ****************************************/

        public RunId(uint value)
        {
            Value = value;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 다음 런의 신원. 0은 "런 없음"이라 건너뛴다 </summary>
        public RunId Next()
        {
            uint next = (Value + 1u);
            return new RunId((next == 0u) ? 1u : next);
        }

        public bool Equals(RunId other) => (Value == other.Value);

        public override bool Equals(object obj) => ((obj is RunId other) && (Equals(other)));

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => Value.ToString();

        public static bool operator ==(RunId left, RunId right) => left.Equals(right);

        public static bool operator !=(RunId left, RunId right) => (!left.Equals(right));
    }
}
