// Unity
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Geumeum.Art
{
    /// <summary>
    /// 카메라 연출 트랙. 바인딩 없이 지금 게임 카메라(CameraRig.Current)에 값을 넣는다
    /// 스킬·컷신 Timeline에 올려 모션과 타이밍을 맞춘다. 게임 카메라가 없는 편집 모드 미리보기에서는 아무것도 안 한다
    /// </summary>
    [TrackColor(0.95f, 0.55f, 0.2f)]
    [TrackClipType(typeof(CameraEffectClip))]
    public class CameraEffectTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<CameraEffectMixer>.Create(graph, inputCount);
        }
    }

    /// <summary> 겹친 클립을 가중치로 합산해 카메라에 한 번 넣는다 </summary>
    public class CameraEffectMixer : PlayableBehaviour
    {
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            CameraRig rig = CameraRig.Current;
            if (rig == null)
            {
                return;
            }

            float distance = 0f;
            float fov      = 0f;
            float dutch    = 0f;

            int inputCount = playable.GetInputCount();

            for (int i = 0; i < inputCount; ++i)
            {
                float weight = playable.GetInputWeight(i);
                if (weight <= 0f)
                {
                    continue;
                }

                var input = (ScriptPlayable<CameraEffectBehaviour>)playable.GetInput(i);
                CameraEffectBehaviour clip = input.GetBehaviour();

                distance += clip.DistanceDelta * weight;
                fov      += clip.FovDelta * weight;
                dutch    += clip.Dutch * weight;
            }

            rig.SetTimelineEffect(distance, fov, dutch);
        }
    }
}
