// System
using System;

namespace Geumeum.Common
{
    /// <summary> 씬 전환의 세대. 서버가 전환을 시작할 때마다 올린다 </summary>
    public readonly struct SceneRevision : IEquatable<SceneRevision>
    {
        /****************************************
        *                Fields
        ****************************************/

        public static readonly SceneRevision None = new SceneRevision(0u);

        public readonly uint Value;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsValid => (Value != 0u);

        /****************************************
        *              Constructors
        ****************************************/

        public SceneRevision(uint value)
        {
            Value = value;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 다음 세대. 0은 "전환한 적 없음"이라 건너뛴다 </summary>
        public SceneRevision Next()
        {
            uint next = (Value + 1u);
            return new SceneRevision((next == 0u) ? 1u : next);
        }

        public bool Equals(SceneRevision other) => (Value == other.Value);

        public override bool Equals(object obj) => ((obj is SceneRevision other) && (Equals(other)));

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => Value.ToString();

        public static bool operator ==(SceneRevision left, SceneRevision right) => left.Equals(right);

        public static bool operator !=(SceneRevision left, SceneRevision right) => (!left.Equals(right));
    }
}
