// System
using System;

namespace Geumeum.Common
{
    /// <summary>
    /// 씬 전환의 세대. 서버가 전환을 시작할 때마다 올린다.
    ///
    /// 런당 전환이 다섯 번이고 각 전환마다 클라이언트의 로드 완료 통지가 돌아온다.
    /// 세대가 없으면 이전 전환의 늦은 통지가 다음 전환을 통과시켜, 아직 로드가 끝나지 않은
    /// 사람을 두고 게임이 시작된다. 씬을 가리키는 참조에도 실어 다른 씬의 netId 재사용을 막는다
    /// </summary>
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
