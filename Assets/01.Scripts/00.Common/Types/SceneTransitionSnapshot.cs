// System
using System;

namespace Geumeum.Common
{
    /// <summary> 씬 전환 한 번의 내용. 전환 소유자가 만들고 각 씬의 바인딩 대상이 읽는다 </summary>
    [Serializable]
    public struct SceneTransitionSnapshot : IEquatable<SceneTransitionSnapshot>
    {
        /****************************************
        *                Fields
        ****************************************/

        public uint       RunIdValue;
        public uint       SceneRevisionValue;
        public RunSceneId FromScene;
        public RunSceneId ToScene;

        /// <summary> 도착한 뒤 참가자 상태를 되부을지 </summary>
        public bool RestoreParticipants;

        /****************************************
        *              Properties
        ****************************************/

        public RunId         RunId    => new RunId(RunIdValue);
        public SceneRevision Revision => new SceneRevision(SceneRevisionValue);

        /****************************************
        *                Methods
        ****************************************/

        public static SceneTransitionSnapshot Create(
            RunId runId, SceneRevision revision, RunSceneId fromScene, RunSceneId toScene, bool restoreParticipants)
        {
            return new SceneTransitionSnapshot
            {
                RunIdValue          = runId.Value,
                SceneRevisionValue  = revision.Value,
                FromScene           = fromScene,
                ToScene             = toScene,
                RestoreParticipants = restoreParticipants,
            };
        }

        public bool Equals(SceneTransitionSnapshot other)
        {
            return ((RunIdValue == other.RunIdValue) &&
                    (SceneRevisionValue == other.SceneRevisionValue) &&
                    (FromScene == other.FromScene) &&
                    (ToScene == other.ToScene) &&
                    (RestoreParticipants == other.RestoreParticipants));
        }

        public override bool Equals(object obj) => ((obj is SceneTransitionSnapshot other) && (Equals(other)));

        public override int GetHashCode() => (RunIdValue.GetHashCode() ^ SceneRevisionValue.GetHashCode());
    }
}
