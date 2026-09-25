// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Art
{
    /// <summary>
    /// 맞은 대상 하나의 표현 캐시(COMBAT_STYLE_DESIGN S1, S2). 내 화면에서만 애니메이터를 잠깐 멈추고 상체를 젖힌다
    /// 위치, 콜라이더, 서버 루트는 건드리지 않는다. 처음 맞았을 때 한 번만 컴포넌트를 찾아 둔다
    /// </summary>
    public sealed class ImpactTargetView
    {
        /****************************************
        *              Constants
        ****************************************/

        // 휴머노이드가 아닌 모델에서 상체 뼈를 이름으로 찾는다
        private const string TORSO_NAME_SPINE = "spine";
        private const string TORSO_NAME_CHEST = "chest";

        /****************************************
        *                Fields
        ****************************************/

        // 휴머노이드가 아닌 대상의 뼈 이름 검색에 재사용한다. 대상마다 처음 맞을 때 배열을 새로 만들지 않는다
        // 메인 스레드에서만 쓰고 검색이 끝나면 비워 파괴된 Transform을 붙잡지 않는다
        private static readonly List<Transform> G_TorsoSearchBuffer = new List<Transform>();

        private readonly NetworkIdentity _identity;
        private readonly GameObject      _gameObject;
        private readonly bool            _isBoss;

        private readonly Animator[] _animators;
        private readonly float[]    _savedSpeeds;
        private readonly bool[]     _stopped;

        private readonly IEnemyPresentationSource _enemy;
        private readonly IDamageHealthSource      _health;
        private readonly IBossPhaseSource         _bossPhase;
        private readonly IBossTelegraphSource     _bossTelegraph;

        private readonly Transform _torso;

        private TargetHitStopLimiter _limiter;

        // 움찔 상태
        private bool       _flinchActive   = false;
        private bool       _hasFlinched    = false;
        private double     _flinchStart    = 0d;
        private float      _flinchSeconds  = 0f;
        private float      _flinchDegrees  = 0f;
        private Vector3    _flinchAxis     = Vector3.zero;

        // 애니메이터가 이번 프레임 뼈를 다시 쓰지 않았을 때(컬링 등) 회전이 쌓이지 않게 원래 자세를 기억한다
        private bool       _hasWritten     = false;
        private Quaternion _writtenLocal   = Quaternion.identity;
        private Quaternion _basePose       = Quaternion.identity;

        /****************************************
        *              Properties
        ****************************************/

        public uint NetId { get; }

        /// <summary> 대상이 파괴됐다. 캐시를 버린다 </summary>
        public bool IsGone => (_identity == null) || (_gameObject == null);

        public bool IsStopped   => _limiter.IsActive;
        public bool IsFlinching => _flinchActive;

        /****************************************
        *              Constructor
        ****************************************/

        public ImpactTargetView(NetworkIdentity identity, bool isBoss)
        {
            _identity   = identity;
            _gameObject = identity.gameObject;
            _isBoss     = isBoss;
            NetId       = identity.netId;

            _animators   = identity.GetComponentsInChildren<Animator>(true);
            _savedSpeeds = new float[_animators.Length];
            _stopped     = new bool[_animators.Length];

            _enemy         = identity.GetComponent<IEnemyPresentationSource>();
            _health        = identity.GetComponent<IDamageHealthSource>();
            _bossPhase     = identity.GetComponentInParent<IBossPhaseSource>();
            _bossTelegraph = identity.GetComponentInParent<IBossTelegraphSource>();

            _torso = isBoss ? null : FindTorso(_animators);
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 대상 멈춤을 요청한다. 상태가 멈추면 안 되는 때면 무시한다 </summary>
        public void RequestStop(double now, float seconds, float maxSeconds)
        {
            if ((seconds <= 0f) || !CanPresent())
            {
                return;
            }

            if (_limiter.Request(now, seconds, maxSeconds))
            {
                BeginStop();
            }
        }

        /// <summary> 움찔을 시작한다. 같은 대상은 guardSeconds 안에 한 번만 </summary>
        public void RequestFlinch(double now, float degrees, float seconds, Vector3 hitDirection, float guardSeconds)
        {
            if (_isBoss || (_torso == null) || (degrees <= 0f) || (seconds <= 0f) || !CanPresent())
            {
                return;
            }

            if (_hasFlinched && ((now - _flinchStart) < guardSeconds))
            {
                return;
            }

            Vector3 axis = ImpactRules.FlinchAxis(hitDirection);
            if (axis == Vector3.zero)
            {
                return;
            }

            _hasFlinched   = true;
            _flinchActive  = true;
            _flinchStart   = now;
            _flinchSeconds = seconds;
            _flinchDegrees = degrees;
            _flinchAxis    = axis;
        }

        /// <summary> 멈춤 시간을 재고 우선 상태로 바뀌었으면 즉시 복원한다 </summary>
        public void TickStop(double now, float cooldownSeconds)
        {
            if (!_limiter.IsActive)
            {
                return;
            }

            if (!CanPresent())
            {
                if (_limiter.Cancel(now, cooldownSeconds))
                {
                    RestoreStop();
                }

                return;
            }

            if (_limiter.Tick(now, cooldownSeconds))
            {
                RestoreStop();
            }
        }

        /// <summary> 애니메이션이 뼈를 쓴 뒤(LateUpdate 후반)에 상체 회전을 얹는다 </summary>
        public void ApplyFlinch(double now)
        {
            if (!_flinchActive)
            {
                return;
            }

            if ((_torso == null) || !CanPresent())
            {
                EndFlinch();
                return;
            }

            Quaternion current  = _torso.localRotation;
            Quaternion basePose = (_hasWritten && (current == _writtenLocal)) ? _basePose : current;

            float weight = ImpactRules.FlinchWeight((float)(now - _flinchStart), _flinchSeconds);
            if (weight <= 0f)
            {
                _torso.localRotation = basePose;
                _flinchActive        = false;
                _hasWritten          = false;
                return;
            }

            _basePose            = basePose;
            _torso.localRotation = basePose;
            _torso.rotation      = Quaternion.AngleAxis(_flinchDegrees * weight, _flinchAxis) * _torso.rotation;
            _writtenLocal        = _torso.localRotation;
            _hasWritten          = true;
        }

        /// <summary> 멈춤과 움찔을 모두 되돌린다. 비활성화, 런 정리 때 부른다 </summary>
        public void RestoreAll(double now, float cooldownSeconds)
        {
            if (_limiter.Cancel(now, cooldownSeconds))
            {
                RestoreStop();
            }

            EndFlinch();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 지금 이 대상에 멈춤, 움찔을 걸어도 되는지 </summary>
        private bool CanPresent()
        {
            if (IsGone || !_gameObject.activeInHierarchy)
            {
                return false;
            }

            if ((_health != null) && (_health.Health <= 0f))
            {
                return false;
            }

            if (_isBoss)
            {
                return IsBossSafe();
            }

            // 잡귀가 아닌 대상(상태를 모르는 것)은 건드리지 않는다
            if (_enemy == null)
            {
                return false;
            }

            return !ImpactRules.IsEnemyBusy(_enemy.VisualState);
        }

        /// <summary>
        /// 보스는 공격 모션의 끝을 클라이언트에서 확실히 알 수 없다. 그로기로 쓰러져 있고 걸린 예고가 없을 때만 안전하다고 본다
        /// </summary>
        private bool IsBossSafe()
        {
            if ((_bossPhase == null) || (_bossPhase.PhaseSnapshot.Stage != BossEncounterStage.Groggy))
            {
                return false;
            }

            if (_bossTelegraph == null)
            {
                return false;
            }

            IReadOnlyList<BossTelegraphCue> cues = _bossTelegraph.TelegraphCues;
            if (cues != null)
            {
                for (int i = 0; i < cues.Count; ++i)
                {
                    if (cues[i].IsActive)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void BeginStop()
        {
            for (int i = 0; i < _animators.Length; ++i)
            {
                Animator animator = _animators[i];
                if ((animator == null) || !animator.isActiveAndEnabled)
                {
                    _stopped[i] = false;
                    continue;
                }

                _savedSpeeds[i] = animator.speed;
                _stopped[i]     = true;
                animator.speed  = 0f;
            }
        }

        private void RestoreStop()
        {
            for (int i = 0; i < _animators.Length; ++i)
            {
                if (!_stopped[i])
                {
                    continue;
                }

                _stopped[i] = false;

                // 파괴된 애니메이터는 되돌릴 것이 없다
                if (_animators[i] != null)
                {
                    _animators[i].speed = _savedSpeeds[i];
                }
            }
        }

        private void EndFlinch()
        {
            if (_hasWritten && (_torso != null) && (_torso.localRotation == _writtenLocal))
            {
                _torso.localRotation = _basePose;
            }

            _flinchActive = false;
            _hasWritten   = false;
        }

        /// <summary> 휴머노이드면 가슴(없으면 척추), 아니면 이름에 spine, chest가 들어간 첫 자식. 없으면 null </summary>
        private static Transform FindTorso(Animator[] animators)
        {
            for (int i = 0; i < animators.Length; ++i)
            {
                Animator animator = animators[i];
                if ((animator == null) || !animator.isHuman)
                {
                    continue;
                }

                Transform bone = animator.GetBoneTransform(HumanBodyBones.Chest);
                if (bone == null)
                {
                    bone = animator.GetBoneTransform(HumanBodyBones.Spine);
                }

                if (bone != null)
                {
                    return bone;
                }
            }

            for (int i = 0; i < animators.Length; ++i)
            {
                if (animators[i] == null)
                {
                    continue;
                }

                // 깊이 우선 순서라 먼저 나오는 척추 뼈가 상체 전체를 움직이는 아래쪽 뼈다
                Transform found = FindTorsoByName(animators[i]);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary> 애니메이터 아래에서 이름에 spine, chest가 들어간 첫 Transform. 없으면 null </summary>
        private static Transform FindTorsoByName(Animator animator)
        {
            animator.GetComponentsInChildren(true, G_TorsoSearchBuffer);

            Transform found = null;
            for (int j = 0; j < G_TorsoSearchBuffer.Count; ++j)
            {
                string name = G_TorsoSearchBuffer[j].name;
                if ((name.IndexOf(TORSO_NAME_SPINE, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (name.IndexOf(TORSO_NAME_CHEST, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    found = G_TorsoSearchBuffer[j];
                    break;
                }
            }

            G_TorsoSearchBuffer.Clear();
            return found;
        }
    }
}
