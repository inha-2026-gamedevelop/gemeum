// Unity
using UnityEngine;

// 서드파티
using Unity.Cinemachine;

// 프로젝트 내부
using Geumeum.Data;

namespace Geumeum.Art
{
    /// <summary>
    /// 카메라 값을 잠깐 바꿨다가 되돌리는 연출. 스킬 타이밍에 거리를 당기고 화각을 넓히는 식이다
    /// 코드(스킬 표현)와 Timeline(CameraEffectTrack) 두 곳에서 들어오고 합산한다. Cinemachine Aim 단계 뒤에 얹는다
    /// </summary>
    [AddComponentMenu("")]
    public class CameraEffectPlayer : CinemachineExtension
    {
        /****************************************
        *              Constants
        ****************************************/

        // 동시에 살아 있을 수 있는 연출 수
        private const int SLOT_COUNT = 4;

        private const float MIN_FOV = 1f;
        private const float MAX_FOV = 179f;

        /****************************************
        *                Types
        ****************************************/

        private struct Slot
        {
            public bool  Active;
            public float Elapsed;
            public CameraConfigSO.CameraEffectSpec Spec;
        }

        /****************************************
        *                Fields
        ****************************************/

        private readonly Slot[] _slots = new Slot[SLOT_COUNT];

        private int _evaluatedFrame = -1;

        // Timeline이 이번 프레임에 넣은 값. 안 넣은 프레임에는 버린다 - 그래프가 멈췄는데 값이 남으면 화면이 굳는다
        private int   _timelineFrame    = -1;
        private float _timelineDistance = 0f;
        private float _timelineFov      = 0f;
        private float _timelineDutch    = 0f;

        // 이번 프레임에 얹을 값
        private float _distance = 0f;
        private float _fov      = 0f;
        private float _dutch    = 0f;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary>
        /// 이번 프레임 붐 길이(m). 리그가 민다. 당길 수 있는 한도가 여기서 나온다 -
        /// 벽에 밀려 이미 짧아진 붐을 더 당기면 캐릭터 머리 속으로 들어간다
        /// </summary>
        public float BoomLength { get; set; } = 0f;

        /// <summary> 이번 프레임에 얹은 거리 변화(m, 양수가 당김) </summary>
        public float CurrentDistance => _distance;

        /// <summary> 이번 프레임에 얹은 화각 변화(도) </summary>
        public float CurrentFov => _fov;

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 연출 하나를 처음부터 재생한다. 자리가 없으면 가장 오래된 것을 밀어낸다 </summary>
        public void Play(in CameraConfigSO.CameraEffectSpec spec)
        {
            if (spec.TotalSeconds <= 0f)
            {
                return;
            }

            int   index  = 0;
            float oldest = float.MinValue;

            for (int i = 0; i < _slots.Length; ++i)
            {
                if (!_slots[i].Active)
                {
                    index = i;
                    break;
                }

                if (_slots[i].Elapsed > oldest)
                {
                    oldest = _slots[i].Elapsed;
                    index  = i;
                }
            }

            _slots[index] = new Slot { Active = true, Elapsed = 0f, Spec = spec };
        }

        /// <summary> Timeline 트랙이 이번 프레임의 합산 값을 넣는다 </summary>
        public void SetTimelineInput(float distance, float fov, float dutch)
        {
            _timelineFrame    = Time.frameCount;
            _timelineDistance = distance;
            _timelineFov      = fov;
            _timelineDutch    = dutch;
        }

        /// <summary> 재생 중인 연출을 전부 끊는다 </summary>
        public void Clear()
        {
            for (int i = 0; i < _slots.Length; ++i)
            {
                _slots[i] = default;
            }

            _timelineFrame = -1;
            _distance      = 0f;
            _fov           = 0f;
            _dutch         = 0f;
        }

        /// <summary> 들어가고, 머물고, 돌아오는 가중치(0~1). 테스트가 직접 부른다 </summary>
        public static float EvaluateWeight(in CameraConfigSO.CameraEffectSpec spec, float elapsed)
        {
            float blendIn  = Mathf.Max(0f, spec.BlendIn);
            float hold     = Mathf.Max(0f, spec.Hold);
            float blendOut = Mathf.Max(0f, spec.BlendOut);

            if ((elapsed < 0f) || (elapsed >= (blendIn + hold + blendOut)))
            {
                return 0f;
            }

            if (elapsed < blendIn)
            {
                return Mathf.SmoothStep(0f, 1f, elapsed / blendIn);
            }

            if (elapsed < (blendIn + hold))
            {
                return 1f;
            }

            return (blendOut <= 0f) ? 0f : Mathf.SmoothStep(1f, 0f, (elapsed - blendIn - hold) / blendOut);
        }

        /****************************************
        *              Cinemachine
        ****************************************/

        protected override void PostPipelineStageCallback(
            CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            if (stage != CinemachineCore.Stage.Aim)
            {
                return;
            }

            CameraConfigSO config = GameDB.IsInitialized ? GameDB.Camera : null;
            if (config == null)
            {
                return;
            }

            if (_evaluatedFrame != Time.frameCount)
            {
                _evaluatedFrame = Time.frameCount;
                Evaluate(config, Mathf.Max(0f, deltaTime));
            }

            if ((_distance == 0f) && (_fov == 0f) && (_dutch == 0f))
            {
                return;
            }

            Quaternion orientation = state.GetCorrectedOrientation();
            Vector3    forward     = orientation * Vector3.forward;

            state.PositionCorrection += forward * ResolveDistance(config, state, -forward);
            state.Lens.FieldOfView    = Mathf.Clamp(state.Lens.FieldOfView + _fov, MIN_FOV, MAX_FOV);
            state.Lens.Dutch         += _dutch;
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Evaluate(CameraConfigSO config, float deltaTime)
        {
            float distance = 0f;
            float fov      = 0f;
            float dutch    = 0f;

            for (int i = 0; i < _slots.Length; ++i)
            {
                if (!_slots[i].Active)
                {
                    continue;
                }

                float weight = EvaluateWeight(_slots[i].Spec, _slots[i].Elapsed);

                distance += _slots[i].Spec.DistanceDelta * weight;
                fov      += _slots[i].Spec.FovDelta * weight;
                dutch    += _slots[i].Spec.Dutch * weight;

                _slots[i].Elapsed += deltaTime;

                if (_slots[i].Elapsed >= _slots[i].Spec.TotalSeconds)
                {
                    _slots[i] = default;
                }
            }

            if (_timelineFrame == Time.frameCount)
            {
                distance += _timelineDistance;
                fov      += _timelineFov;
                dutch    += _timelineDutch;
            }

            float scale = config.EffectScale;

            _distance = Mathf.Clamp(distance, -config.EffectMaxDistance, config.EffectMaxDistance) * scale;
            _fov      = Mathf.Clamp(fov, -config.EffectMaxFov, config.EffectMaxFov) * scale;
            _dutch    = dutch * scale;
        }

        /// <summary> 당기기는 최소 거리까지, 빼기는 벽에 닿기 전까지만 허용한다 </summary>
        private float ResolveDistance(CameraConfigSO config, in CameraState state, Vector3 backward)
        {
            if (_distance > 0f)
            {
                float room = Mathf.Max(0f, BoomLength - config.MinDistance);
                return Mathf.Min(_distance, room);
            }

            if (_distance < 0f)
            {
                float wanted = -_distance;

                bool isBlocked = Physics.SphereCast(
                    state.GetCorrectedPosition(),
                    config.ProbeRadius,
                    backward,
                    out RaycastHit hit,
                    wanted,
                    config.CollisionMask,
                    QueryTriggerInteraction.Ignore);

                return isBlocked ? -hit.distance : _distance;
            }

            return 0f;
        }
    }
}
