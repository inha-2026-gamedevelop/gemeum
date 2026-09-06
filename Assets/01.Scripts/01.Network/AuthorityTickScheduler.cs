// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Geumeum.Network
{
    /// <summary>
    /// 서버 전용 공용 시뮬레이션 틱. 등록된 콜백을 우선순위 순서로 매 틱 실행한다.
    /// RunManager는 phase, 타이머 책임만 유지하고, 플레이어, AI, 투사체 실행 순서는 여기서 관리한다.
    /// Rigidbody 조작은 물리 스텝 안에서만 안전하므로 FixedUpdate를 틱 경계로 삼는다.
    /// 물리 주기는 ProjectSettings의 Fixed Timestep이며 SERVER_TICK_INTERVAL과 같은 값으로 맞춰 둔다
    /// </summary>
    public class AuthorityTickScheduler : NetworkBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        private struct TickCallback
        {
            public int           Priority;
            public Action<float> Callback;
        }

        /****************************************
        *                Fields
        ****************************************/

        private readonly List<TickCallback>     _callbacks       = new List<TickCallback>();

        // 순회 중 등록, 해제가 섞이면 우선순위가 무시되거나 콜백이 그 틱에서 스킵된다.
        // 등록은 다음 FixedUpdate 시작에서 일괄 반영하고, 해제는 즉시 무효 표시만 한 뒤
        // 순회가 끝난 다음 목록에서 지운다
        private readonly List<TickCallback>     _pendingAdds     = new List<TickCallback>();
        private readonly HashSet<Action<float>> _pendingRemovals = new HashSet<Action<float>>();

        // 정지 게이트를 지나지 않는 제어 콜백. 이탈로 멈춰 있는 동안에도 돌아야 하는 것이 여기 붙는다 -
        // 종료 후보 확정이 시뮬레이션과 함께 멈추면 대기를 끊어도 런이 끝나지 않는다
        private System.Action _controlTick = null;

        private bool _isSorted             = true;
        private bool _isTicking            = false;
        private bool _isSimulationEnabled  = true; // Result 확정 뒤 멱등하게 전체 gameplay tick을 정지한다

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버 프로세스의 인스턴스, 클라이언트에서는 null이다 </summary>
        public static AuthorityTickScheduler ServerInstance { get; private set; }

        /// <summary>
        /// 서버 시뮬레이션 틱의 번호. FixedUpdate마다 하나씩 오른다.
        ///
        /// 같은 물리 스텝 안에서 일어난 판정은 같은 번호에 속한다.
        /// "이번 틱에 들어온 후보"와 "지난 틱에 들어온 후보"를 가르는 데 쓴다
        /// </summary>
        public ulong TickId { get; private set; } = 0UL;

        /****************************************
        *              Unity Event
        ****************************************/

        // 물리 스텝이 곧 서버 틱이다. Rigidbody를 만지는 콜백이 여기 밖에서 돌면 이동이 물리와 어긋난다
        private void FixedUpdate()
        {
            if (!_isTicking)
            {
                return;
            }

            ++TickId;

            // 등록, 해제 반영은 시뮬레이션이 멈춰 있어도 한다. 이 처리가 정지 게이트 안에 있으면
            // Result 구간 동안 큐가 무한정 자라고, 그사이 파괴된 오브젝트의 델리게이트가
            // 다음 런을 켜는 순간 한꺼번에 되살아난다
            FlushPendingChanges();

            // 제어 콜백은 정지 게이트보다 앞이다. 멈춰 있는 동안 종료를 확정할 수 있어야 한다
            _controlTick?.Invoke();

            if (!_isSimulationEnabled)
            {
                return;
            }

            if (!_isSorted)
            {
                _callbacks.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                _isSorted = true;
            }

            for (int i = 0; i < _callbacks.Count; ++i)
            {
                Action<float> callback = _callbacks[i].Callback;
                if (_pendingRemovals.Contains(callback))
                {
                    continue; // 이번 틱 중 해제 요청된 콜백, 이미 무효라 호출하지 않는다
                }

                callback.Invoke(Time.fixedDeltaTime);
            }

            // 순회 중 들어온 요청을 여기서 정리한다. 순회 도중 목록을 건드리면 인덱스가 어긋난다
            FlushPendingChanges();
        }

        /****************************************
        *                Methods
        ****************************************/

        // 등록을 먼저, 해제를 나중에 반영한다. 같은 창에 등록과 해제가 함께 들어오면 해제가 이겨야 한다 -
        // 델리게이트가 같으면 RemoveAll이 방금 넣은 항목까지 지우므로 순서를 뒤집으면 유령 등록이 남는다
        private void FlushPendingChanges()
        {
            if (_pendingAdds.Count > 0)
            {
                _callbacks.AddRange(_pendingAdds);
                _pendingAdds.Clear();
                _isSorted = false;
            }

            if (_pendingRemovals.Count > 0)
            {
                _callbacks.RemoveAll(entry => _pendingRemovals.Contains(entry.Callback));
                _pendingRemovals.Clear();
            }
        }

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            ServerInstance         = this;
            _isTicking             = true;
            _isSimulationEnabled   = true;
        }

        public override void OnStopServer()
        {
            _isTicking   = false;
            _controlTick = null;
            _callbacks.Clear();
            _pendingAdds.Clear();
            _pendingRemovals.Clear();

            if (ServerInstance == this)
            {
                ServerInstance = null;
            }
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary>
        /// 우선순위가 낮을수록 먼저 실행된다. deltaTime은 물리 고정 스텝이 전달된다.
        /// 순회 도중 호출해도 이번 틱에는 반영되지 않고 다음 FixedUpdate부터 실행된다
        /// </summary>
        [Server]
        public void Register(int priority, Action<float> callback)
        {
            _pendingAdds.Add(new TickCallback { Priority = priority, Callback = callback });
        }

        /// <summary>
        /// 순회 도중 호출해도 그 자리에서 즉시 무효 처리되어 이번 틱에 더 이상 실행되지 않고,
        /// 실제 목록 정리는 순회가 끝난 뒤 한 번에 한다
        /// </summary>
        [Server]
        public void Unregister(Action<float> callback)
        {
            _pendingRemovals.Add(callback);
        }

        /// <summary>
        /// 정지 게이트를 지나지 않는 제어 콜백을 등록한다. 멈춰 있는 동안에도 매 틱 불린다.
        /// 이동, AI, 자원 회복처럼 게임 진행에 속하는 것은 여기 넣지 않는다
        /// </summary>
        [Server]
        public void RegisterControl(System.Action callback)
        {
            if (callback == null)
            {
                return;
            }

            _controlTick += callback;
        }

        /// <summary> 제어 콜백을 해제한다 </summary>
        [Server]
        public void UnregisterControl(System.Action callback)
        {
            if (callback == null)
            {
                return;
            }

            _controlTick -= callback;
        }

        /// <summary>
        /// 등록을 건드리지 않고 전체 gameplay tick만 멱등하게 껐다 켠다.
        /// Result 확정 뒤 이동, AI, 자원 회복을 한 번에 멈추고, 새 런 시작 때만 다시 켠다
        /// </summary>
        [Server]
        public void SetSimulationEnabled(bool isEnabled)
        {
            _isSimulationEnabled = isEnabled;
        }
    }
}
