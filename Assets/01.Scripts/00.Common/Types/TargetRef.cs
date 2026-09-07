// System
using System;

namespace Geumeum.Common
{
    /// <summary> 피해와 표식, 락온, 화살이 함께 가리키는 대상. 런과 씬 세대, netId, 부위 id로 지목한다 </summary>
    [Serializable]
    public struct TargetRef : IEquatable<TargetRef>
    {
        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 부위가 아니라 본체를 가리키는 값 </summary>
        public const byte BODY_PART_ID = 0;

        public uint RunIdValue;
        public uint SceneRevisionValue;
        public uint EntityNetId;

        /// <summary> 0은 본체, 그 밖의 값은 부위. 꼬리 세 덩이가 여기서 갈린다 </summary>
        public byte PartId;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsValid => (EntityNetId != 0u);

        /// <summary> 부위가 아니라 본체를 가리키는지 </summary>
        public bool IsBody => (PartId == BODY_PART_ID);

        /****************************************
        *                Methods
        ****************************************/

        public static TargetRef Create(RunId runId, SceneRevision sceneRevision, uint entityNetId, byte partId)
        {
            return new TargetRef
            {
                RunIdValue         = runId.Value,
                SceneRevisionValue = sceneRevision.Value,
                EntityNetId        = entityNetId,
                PartId             = partId,
            };
        }

        /// <summary> 이 참조가 지금 런과 지금 씬의 것인지 </summary>
        public bool IsCurrent(RunId runId, SceneRevision sceneRevision)
        {
            return ((RunIdValue == runId.Value) && (SceneRevisionValue == sceneRevision.Value));
        }

        public bool Equals(TargetRef other)
        {
            return ((RunIdValue == other.RunIdValue) &&
                    (SceneRevisionValue == other.SceneRevisionValue) &&
                    (EntityNetId == other.EntityNetId) &&
                    (PartId == other.PartId));
        }

        public override bool Equals(object obj) => ((obj is TargetRef other) && (Equals(other)));

        public override int GetHashCode()
        {
            return (int) (RunIdValue ^ SceneRevisionValue ^ EntityNetId ^ PartId);
        }

        public override string ToString()
        {
            return ("run=" + RunIdValue + " scene=" + SceneRevisionValue + " net=" + EntityNetId + " part=" + PartId);
        }
    }
}
