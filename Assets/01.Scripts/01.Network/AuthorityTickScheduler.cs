// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Geumeum.Network
{
    /// <summary> 서버 전용 공용 시뮬레이션 틱. 등록된 콜백을 우선순위 순서로 매 틱 실행한다 </summary>
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

        private readonly List<TickCallback>     _pendingAdds     = new List<TickCallback>();
        private readonly HashSet<Action<float>> _pendingRemovals = new HashSet<Action<float>>();

        // 정지 게이트를 지나지 않는 제어 콜백
        private System.Action _controlTick = null;

        private bool _isSorted             = true;
        private bool _isTicking            = false;
        private bool _isSimulationEnabled  = true;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버 프로세스의 인스턴스, 클라이언트에서는 null이다 </summary>
        public static AuthorityTickScheduler ServerInstance { get; private set; }

        /// <summary> 서버 시뮬레이션 틱의 번호. FixedUpdate마다 하나씩 오른다 </summary>
        public ulong TickId { get; private set; } = 0UL;

        /****************************************
        *              Unity Event
        ****************************************/

        private void FixedUpdate()
        {
            if (!_isTicking)
            {
                return;
            }

            ++TickId;

            FlushPendingChanges();

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
                    continue;
                }

                callback.Invoke(Time.fixedDeltaTime);
            }

            FlushPendingChanges();
        }

        /****************************************
        *                Methods
        ****************************************/

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

        /// <summary> 콜백을 등록한다. 우선순위가 낮을수록 먼저 실행된다 </summary>
        [Server]
        public void Register(int priority, Action<float> callback)
        {
            _pendingAdds.Add(new TickCallback { Priority = priority, Callback = callback });
        }

        /// <summary> 콜백을 해제한다 </summary>
        [Server]
        public void Unregister(Action<float> callback)
        {
            _pendingRemovals.Add(callback);
        }

        /// <summary> 정지 게이트를 지나지 않는 제어 콜백을 등록한다 </summary>
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

        /// <summary> 전체 gameplay tick을 껐다 켠다 </summary>
        [Server]
        public void SetSimulationEnabled(bool isEnabled)
        {
            _isSimulationEnabled = isEnabled;
        }
    }
}
