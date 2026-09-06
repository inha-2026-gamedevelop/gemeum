// System
using System;

namespace Geumeum.Common
{
    /// <summary>
    /// 참가자의 플레이어 오브젝트 세대. 재접속과 씬 전환으로 오브젝트를 다시 만들 때마다 올린다.
    ///
    /// 홀드 중이던 구조, 보내 둔 장비 요청, 아직 도착하지 않은 스냅샷 확인이
    /// 새 오브젝트에 그대로 적용되면 안 된다. 요청에 이 값을 실어 두면
    /// 서버가 "지금 몸의 것인가"를 가릴 수 있다
    /// </summary>
    public readonly struct BindingGeneration : IEquatable<BindingGeneration>
    {
        /****************************************
        *                Fields
        ****************************************/

        public static readonly BindingGeneration None = new BindingGeneration(0u);

        public readonly uint Value;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsValid => (Value != 0u);

        /****************************************
        *              Constructors
        ****************************************/

        public BindingGeneration(uint value)
        {
            Value = value;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 다음 세대. 0은 "바인딩 없음"이라 건너뛴다 </summary>
        public BindingGeneration Next()
        {
            uint next = (Value + 1u);
            return new BindingGeneration((next == 0u) ? 1u : next);
        }

        public bool Equals(BindingGeneration other) => (Value == other.Value);

        public override bool Equals(object obj) => ((obj is BindingGeneration other) && (Equals(other)));

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => Value.ToString();

        public static bool operator ==(BindingGeneration left, BindingGeneration right) => left.Equals(right);

        public static bool operator !=(BindingGeneration left, BindingGeneration right) => (!left.Equals(right));
    }
}
