// Unity
using UnityEngine;

// 서드파티
using Unity.Cinemachine;

// 프로젝트 내부
using Geumeum.Data;

namespace Geumeum.Art
{
    /// <summary>
    /// 프레임이 달라도 같은 세기로 흔들리는 카메라 셰이크. Cinemachine Noise 단계 뒤에 보정값으로 얹는다
    /// 종류마다 방향·박자·감쇠가 다르고, 같은 종류도 매번 방향·박자·첫 부호를 조금씩 흔들어 똑같이 반복되지 않는다
    /// </summary>
    [AddComponentMenu("")]
    public class FrameIndependentShake : CinemachineExtension
    {
        /****************************************
        *              Constants
        ****************************************/

        // 동시에 살아 있을 수 있는 셰이크 수
        private const int SLOT_COUNT = 6;

        // 옛 에셋처럼 박자·감쇠가 비어 있을 때 쓰는 값
        private const float FALLBACK_FREQUENCY   = 18f;
        private const float FALLBACK_DECAY_POWER = 1f;

        // 같은 종류라도 매번 조금씩 달라지게 하는 폭
        private const float FREQUENCY_JITTER = 0.12f; // ±12%
        private const float DIRECTION_JITTER = 0.3f;  // 주 방향에 섞는 무작위 벡터 길이

        // 주 진동과 박자가 맞물리지 않도록 정수비를 피한 비율 - 맞물리면 같은 궤적이 반복된다
        private const float LATERAL_FREQUENCY_RATIO  = 0.71f;
        private const float ROTATION_FREQUENCY_RATIO = 0.83f;

        // 앞뒤 흔들림에 섞는 위 방향 - 순수한 앞뒤는 4.5m 뒤에서 거의 안 보인다
        private const float FORWARD_LIFT = 0.35f;

        private const float MIN_FOV = 1f;
        private const float MAX_FOV = 179f;

        /****************************************
        *                Types
        ****************************************/

        private struct Slot
        {
            public bool    Active;
            public bool    Fresh;          // 이번 프레임에 들어와 아직 시간을 안 흘렸다
            public float   Elapsed;
            public float   Frequency;
            public Vector3 Primary;        // 카메라 기준 주 방향
            public Vector3 Side;           // 주 방향에 수직인 옆 방향
            public Vector3 RotationAxis;   // (pitch, yaw, roll) 가중치
            public CameraConfigSO.ShakeSpec Spec;

            public PeakPreservingOscillator Main;
            public PeakPreservingOscillator Lateral;
            public PeakPreservingOscillator Turn;
        }

        /****************************************
        *                Fields
        ****************************************/

        private readonly Slot[] _slots = new Slot[SLOT_COUNT];

        private int _evaluatedFrame = -1;

        // 이번 프레임에 얹을 값(카메라 기준)
        private Vector3 _offset = Vector3.zero;
        private Vector3 _euler  = Vector3.zero;
        private float   _fov    = 0f;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 이번 프레임에 얹은 위치 흔들림(카메라 기준, m) </summary>
        public Vector3 CurrentOffset => _offset;

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 셰이크를 하나 더한다. 자리가 없으면 가장 약하게 남은 것을 밀어낸다 </summary>
        public void Add(in CameraConfigSO.ShakeSpec spec)
        {
            if ((spec.Duration <= 0f) ||
                ((spec.Amplitude <= 0f) && (spec.RotationDegrees <= 0f) && Mathf.Approximately(spec.FovKick, 0f)))
            {
                return;
            }

            int index = FindSlot();

            float frequency = (spec.Frequency > 0f) ? spec.Frequency : FALLBACK_FREQUENCY;
            frequency *= 1f + Random.Range(-FREQUENCY_JITTER, FREQUENCY_JITTER);

            ResolveDirections(spec.Direction, out Vector3 primary, out Vector3 side, out Vector3 rotationAxis);

            _slots[index] = new Slot
            {
                Active       = true,
                Fresh        = true,
                Elapsed      = 0f,
                Frequency    = frequency,
                Primary      = primary,
                Side         = side,
                RotationAxis = rotationAxis,
                Spec         = spec,
                Main         = PeakPreservingOscillator.StartAtPeak(Random.value < 0.5f),
                Lateral      = PeakPreservingOscillator.StartAtPeak(Random.value < 0.5f),
                Turn         = PeakPreservingOscillator.StartAtPeak(Random.value < 0.5f),
            };
        }

        /// <summary> 살아 있는 셰이크를 전부 끊는다. 관전 전환처럼 화면이 통째로 바뀔 때 쓴다 </summary>
        public void Clear()
        {
            for (int i = 0; i < _slots.Length; ++i)
            {
                _slots[i] = default;
            }

            _offset = Vector3.zero;
            _euler  = Vector3.zero;
            _fov    = 0f;
        }

        /****************************************
        *              Cinemachine
        ****************************************/

        protected override void PostPipelineStageCallback(
            CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            if (stage != CinemachineCore.Stage.Noise)
            {
                return;
            }

            // 전환 중에는 한 프레임에 두 번 불릴 수 있다. 시간은 한 번만 흘린다
            if (_evaluatedFrame != Time.frameCount)
            {
                _evaluatedFrame = Time.frameCount;
                Evaluate(Mathf.Max(0f, deltaTime));
            }

            if ((_offset == Vector3.zero) && (_euler == Vector3.zero) && (_fov == 0f))
            {
                return;
            }

            Quaternion orientation = state.GetCorrectedOrientation();

            state.PositionCorrection   += orientation * _offset;
            state.OrientationCorrection = state.OrientationCorrection * Quaternion.Euler(_euler);
            state.Lens.FieldOfView      = Mathf.Clamp(state.Lens.FieldOfView + _fov, MIN_FOV, MAX_FOV);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Evaluate(float deltaTime)
        {
            _offset = Vector3.zero;
            _euler  = Vector3.zero;
            _fov    = 0f;

            CameraConfigSO config = GameDB.IsInitialized ? GameDB.Camera : null;
            if (config == null)
            {
                return;
            }

            for (int i = 0; i < _slots.Length; ++i)
            {
                if (!_slots[i].Active)
                {
                    continue;
                }

                AccumulateSlot(ref _slots[i], deltaTime);
            }

            float scale = config.ShakeScale;
            if (scale <= 0f)
            {
                _offset = Vector3.zero;
                _euler  = Vector3.zero;
                _fov    = 0f;
                return;
            }

            _offset = Vector3.ClampMagnitude(_offset, config.ShakeMaxAmplitude) * scale;
            _euler  = Vector3.ClampMagnitude(_euler, config.ShakeMaxRotationDegrees) * scale;
            _fov    = Mathf.Clamp(_fov, -config.ShakeMaxFovKick, config.ShakeMaxFovKick) * scale;
        }

        private void AccumulateSlot(ref Slot slot, float deltaTime)
        {
            // 첫 프레임은 시간을 흘리지 않고 극값을 그대로 보인다 - 낮은 프레임에서 한 프레임 만에
            // 감쇠가 다 빠져 버리면 타격 순간이 비어 보인다
            float step = slot.Fresh ? 0f : deltaTime;
            slot.Fresh = false;

            slot.Elapsed += step;

            if (slot.Elapsed >= slot.Spec.Duration)
            {
                slot = default;
                return;
            }

            float decay    = (slot.Spec.DecayPower > 0f) ? slot.Spec.DecayPower : FALLBACK_DECAY_POWER;
            float envelope = Mathf.Pow(1f - (slot.Elapsed / slot.Spec.Duration), decay);

            float main    = slot.Main.Step(slot.Frequency, step);
            float lateral = slot.Lateral.Step(slot.Frequency * LATERAL_FREQUENCY_RATIO, step);
            float turn    = slot.Turn.Step(slot.Frequency * ROTATION_FREQUENCY_RATIO, step);

            Vector3 position = (slot.Primary * main) + (slot.Side * (lateral * Mathf.Clamp01(slot.Spec.Lateral)));

            _offset += position * (slot.Spec.Amplitude * envelope);
            _euler  += slot.RotationAxis * (turn * slot.Spec.RotationDegrees * envelope);

            // 화각은 떨지 않고 한 번 찍혔다가 돌아온다
            _fov += slot.Spec.FovKick * envelope;
        }

        private int FindSlot()
        {
            int   weakest          = 0;
            float weakestRemaining = float.MaxValue;

            for (int i = 0; i < _slots.Length; ++i)
            {
                if (!_slots[i].Active)
                {
                    return i;
                }

                float remaining = _slots[i].Spec.Amplitude * (1f - (_slots[i].Elapsed / _slots[i].Spec.Duration));
                if (remaining < weakestRemaining)
                {
                    weakestRemaining = remaining;
                    weakest          = i;
                }
            }

            return weakest;
        }

        /// <summary> 방향 종류를 카메라 기준 벡터로 바꾸고 매번 조금 비튼다 </summary>
        private static void ResolveDirections(CameraConfigSO.ShakeDirection direction,
            out Vector3 primary, out Vector3 side, out Vector3 rotationAxis)
        {
            switch (direction)
            {
                case CameraConfigSO.ShakeDirection.Vertical:
                    primary      = Vector3.up;
                    rotationAxis = new Vector3(1f, 0f, 0.4f);
                    break;

                case CameraConfigSO.ShakeDirection.Horizontal:
                    primary      = Vector3.right;
                    rotationAxis = new Vector3(0f, 0.5f, 1f);
                    break;

                case CameraConfigSO.ShakeDirection.Forward:
                    primary      = new Vector3(0f, FORWARD_LIFT, 1f).normalized;
                    rotationAxis = new Vector3(1f, 0f, 0.25f);
                    break;

                default:
                    Vector2 planar = Random.insideUnitCircle.normalized;
                    primary        = (planar == Vector2.zero) ? Vector3.up : new Vector3(planar.x, planar.y, 0f);
                    rotationAxis   = new Vector3(Random.value, Random.value * 0.5f, Random.value);
                    break;
            }

            primary = (primary + (Random.insideUnitSphere * DIRECTION_JITTER)).normalized;

            // 주 방향에 수직인 옆 방향. 앞뒤 흔들림이면 화면 오른쪽, 아니면 화면 평면 안에서 수직
            side = Vector3.Cross(Vector3.forward, primary);
            if (side.sqrMagnitude < 1e-4f)
            {
                side = Vector3.right;
            }

            side         = side.normalized;
            rotationAxis = (rotationAxis.sqrMagnitude > 0f) ? rotationAxis.normalized : Vector3.forward;
        }
    }
}
