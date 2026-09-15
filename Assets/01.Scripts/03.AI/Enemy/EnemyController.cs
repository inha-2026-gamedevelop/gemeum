// System
using System;
using System.Collections;

// Unity
using UnityEngine;
using UnityEngine.AI;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Dev;
using Geumeum.Network;
using Geumeum.Systems;

namespace Geumeum.AI
{
    /// <summary>
    /// 잡귀 공통 실행부. Brain의 판단을 받아 실제 추적 · 공격을 수행하고 사망 이벤트를 발행한다.
    /// EnemyKind로 알맞은 Brain을 골라 데이터 구동한다, 종류별 상속은 쓰지 않는다
    /// </summary>
    [RequireComponent(typeof(EnemyStatus))]
    [RequireComponent(typeof(NavMeshAgent))]
    public partial class EnemyController : NetworkBehaviour, IPrecisionInterruptTarget,
        IEnemyPresentationSource
    {

        [SerializeField] private EnemyStatus     _status;
        [SerializeField] private NavMeshAgent    _agent;
        [SerializeField] private CurseProjectile _projectilePrefab; // 원거리형만 배정, 근접형은 비워둔다

        /****************************************
        *                Events
        ****************************************/

        /// <summary> 표현 전용 컴포넌트(EnemyAnimationController)가 구독한다. 서버 · 클라이언트 모두에서 값이 바뀔 때마다 발생한다 </summary>
        public event Action<EnemyVisualState> VisualStateChanged;

        /// <summary> 공격 모션 인덱스가 바뀔 때마다 발생한다, VisualStateChanged와 같은 틱에 함께 갱신된다 </summary>
        public event Action<int> AttackVariantChanged;

        /// <summary> 실제 패링 판정 구간(근사치) 진입 · 이탈마다 발생한다, 표현 전용 </summary>
        public event Action<bool> ParryWindowVisuallyOpenChanged;

        public EnemyVisualState VisualState => _visualState;
        public int AttackVariant => _attackVariant;
        public bool IsParryWindowVisuallyOpen => _isParryWindowVisuallyOpen;

        public bool IsHerding => (_brain != null) && _brain.IsHerding;
        public bool HerdingPathFailed => _herdingPathFailed;
        public TargetRef PrecisionTargetRef => CreateBodyTargetRef();

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_status == null)
            {
                _status = GetComponent<EnemyStatus>();
            }

            if (_agent == null)
            {
                _agent = GetComponent<NavMeshAgent>();
            }

            _playerLayerMask = LayerMask.GetMask(Constants.Layer.PLAYER);
        }

        private TargetRef CreateBodyTargetRef()
        {
            RunManager       run      = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            return TargetRef.Create(
                (run == null) ? default : run.RunId,
                (director == null) ? default : director.Revision,
                netId,
                TargetRef.BODY_PART_ID);
        }

        /****************************************
        *             SyncVar Hooks
        ****************************************/

        /// <summary> _visualState 동기화 훅, 서버 · 클라이언트 양쪽에서 값이 바뀔 때 호출된다 </summary>
        private void OnVisualStateChanged(EnemyVisualState oldState, EnemyVisualState newState)
        {
            VisualStateChanged?.Invoke(newState);
        }

        /// <summary> _attackVariant 동기화 훅, 서버 · 클라이언트 양쪽에서 값이 바뀔 때 호출된다 </summary>
        private void OnAttackVariantChanged(int oldVariant, int newVariant)
        {
            AttackVariantChanged?.Invoke(newVariant);
        }

        /// <summary> _isParryWindowVisuallyOpen 동기화 훅, 서버 · 클라이언트 양쪽에서 값이 바뀔 때 호출된다 </summary>
        private void OnParryWindowVisuallyOpenChanged(bool oldValue, bool newValue)
        {
            ParryWindowVisuallyOpenChanged?.Invoke(newValue);
        }
    }
}
