// System
using System;

// Unity
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Geumeum.Art
{
    /// <summary>
    /// Timeline에 놓는 카메라 연출 클립. 클립 구간 동안 거리·화각·기울기를 바꾸고 Ease In/Out으로 섞는다
    /// 값은 이 클립이 놓인 연출 한 편의 것이라 밸런스 SO가 아니라 Timeline 에셋에 둔다
    /// </summary>
    [Serializable]
    public class CameraEffectClip : PlayableAsset, ITimelineClipAsset
    {
        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("붐을 당기는 거리(m). 양수면 가까워지고 음수면 멀어진다")]
        [SerializeField] private float _distanceDelta = 0.5f;

        [Tooltip("화각 변화(도). 양수면 넓어진다")]
        [SerializeField] private float _fovDelta = 3f;

        [Tooltip("화면 기울기(도)")]
        [SerializeField] private float _dutch = 0f;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 가장자리 섞기(Ease In/Out)와 클립 겹침 섞기를 쓴다 </summary>
        public ClipCaps clipCaps => ClipCaps.Blending;

        /****************************************
        *              Public API
        ****************************************/

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<CameraEffectBehaviour> playable = ScriptPlayable<CameraEffectBehaviour>.Create(graph);

            CameraEffectBehaviour behaviour = playable.GetBehaviour();
            behaviour.DistanceDelta = _distanceDelta;
            behaviour.FovDelta      = _fovDelta;
            behaviour.Dutch         = _dutch;

            return playable;
        }
    }

    /// <summary> 클립 하나의 값. 믹서가 가중치를 곱해 합산한다 </summary>
    public class CameraEffectBehaviour : PlayableBehaviour
    {
        public float DistanceDelta;
        public float FovDelta;
        public float Dutch;
    }
}
