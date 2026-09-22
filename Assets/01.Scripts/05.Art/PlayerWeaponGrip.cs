// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Art
{
    /// <summary> 애니메이션 적용 뒤 손가락 관절을 현재 무기의 파지 각도로 보정한다 </summary>
    // PlayerAnimationController(0)의 정지 프레임 재평가 뒤, PlayerCombatPresentation(100) 앞이다.
    [DefaultExecutionOrder(50)]
    public sealed class PlayerWeaponGrip : MonoBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        public struct FingerJoint
        {
            public Transform  Bone;
            public Quaternion RestLocalRotation;
            public Vector3    CurlAxis;

            [Tooltip("X=검, Y=활, Z=방울 파지 각도(도)")]
            public Vector3 WeaponCurlDegrees;
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private FingerJoint[] _fingerJoints = Array.Empty<FingerJoint>();
        [SerializeField] private Transform     _wrist;
        [SerializeField] private Vector3       _swordWristEuler;
        [SerializeField] private Vector3       _bowWristEuler;
        [SerializeField] private Vector3       _bellWristEuler;

        private WeaponKind _weapon;
        private bool       _isGripActive;
        private Animator   _animator;

        private static readonly int G_BowAttack = Animator.StringToHash("BowAttack");
        private static readonly int G_BowDraw = Animator.StringToHash("BowDraw");
        private static readonly int G_BowHold = Animator.StringToHash("BowHold");
        private static readonly int G_BowRelease = Animator.StringToHash("BowRelease");

        private Transform  _offsetWrist;
        private Quaternion _animatedWristLocalRotation;
        private bool       _hasAppliedWristOffset;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            RestoreWristRotation();
        }

        private void LateUpdate()
        {
            if (!_isGripActive)
            {
                return;
            }

            if (_fingerJoints != null)
            {
                for (int i = 0; i < _fingerJoints.Length; ++i)
                {
                    FingerJoint joint = _fingerJoints[i];
                    if (joint.Bone == null)
                    {
                        continue;
                    }

                    float curlDegrees = ResolveCurlDegrees(in joint, _weapon);
                    joint.Bone.localRotation = joint.RestLocalRotation;
                    if ((curlDegrees != 0f) &&
                        (joint.CurlAxis.sqrMagnitude > Mathf.Epsilon))
                    {
                        joint.Bone.localRotation *= Quaternion.AngleAxis(
                            curlDegrees, joint.CurlAxis);
                    }
                }
            }

            ApplyWristOffset();
        }

        private void OnDisable()
        {
            ClearGrip();
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 다음 LateUpdate부터 지정한 무기의 파지 각도를 적용한다 </summary>
        public void ConfigureWeapon(WeaponKind weapon)
        {
            RestoreWristRotation();
            if (_animator == null)
            {
                _animator = GetComponent<Animator>();
            }
            _weapon       = weapon;
            _isGripActive = true;
        }

        /// <summary> 모든 손가락을 직렬화된 원래 회전으로 되돌리고 파지 보정을 멈춘다 </summary>
        public void ClearGrip()
        {
            _isGripActive = false;
            RestoreWristRotation();

            if (_fingerJoints == null)
            {
                return;
            }

            for (int i = 0; i < _fingerJoints.Length; ++i)
            {
                FingerJoint joint = _fingerJoints[i];
                if (joint.Bone != null)
                {
                    joint.Bone.localRotation = joint.RestLocalRotation;
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void ApplyWristOffset()
        {
            if (_wrist == null)
            {
                return;
            }

            Vector3 wristEuler = ResolveWristEuler(_weapon);
            if (wristEuler == Vector3.zero)
            {
                return;
            }

            _offsetWrist = _wrist;
            _animatedWristLocalRotation = _wrist.localRotation;
            _hasAppliedWristOffset = true;
            // 조준용 손목 회전을 팔을 내린 대기/구르기에도 적용하면 손목이 꺾인다.
            // Animator 전환 가중치에 맞춰 조준 상태에서만 보정한다.
            float weight = _weapon == WeaponKind.Bow ? ResolveBowAimWeight() : 1f;
            _wrist.localRotation = _animatedWristLocalRotation
                * Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(wristEuler), weight);
        }

        private float ResolveBowAimWeight()
        {
            if ((_animator == null) || (_animator.runtimeAnimatorController == null))
            {
                return 1f;
            }

            float current = IsBowAimState(_animator.GetCurrentAnimatorStateInfo(0).shortNameHash) ? 1f : 0f;
            if (!_animator.IsInTransition(0))
            {
                return current;
            }

            float next = IsBowAimState(_animator.GetNextAnimatorStateInfo(0).shortNameHash) ? 1f : 0f;
            return Mathf.Lerp(current, next, Mathf.Clamp01(_animator.GetAnimatorTransitionInfo(0).normalizedTime));
        }

        private static bool IsBowAimState(int state)
        {
            return (state == G_BowAttack) || (state == G_BowDraw)
                || (state == G_BowHold) || (state == G_BowRelease);
        }

        private void RestoreWristRotation()
        {
            if (!_hasAppliedWristOffset)
            {
                return;
            }

            if (_offsetWrist != null)
            {
                _offsetWrist.localRotation = _animatedWristLocalRotation;
            }

            _offsetWrist = null;
            _hasAppliedWristOffset = false;
        }

        private static float ResolveCurlDegrees(in FingerJoint joint,
            WeaponKind weapon)
        {
            switch (weapon)
            {
                case WeaponKind.Sword:
                    return joint.WeaponCurlDegrees.x;

                case WeaponKind.Bow:
                    return joint.WeaponCurlDegrees.y;

                case WeaponKind.Bell:
                    return joint.WeaponCurlDegrees.z;

                default:
                    return 0f;
            }
        }

        private Vector3 ResolveWristEuler(WeaponKind weapon)
        {
            switch (weapon)
            {
                case WeaponKind.Sword:
                    return _swordWristEuler;

                case WeaponKind.Bow:
                    return _bowWristEuler;

                case WeaponKind.Bell:
                    return _bellWristEuler;

                default:
                    return Vector3.zero;
            }
        }
    }
}
