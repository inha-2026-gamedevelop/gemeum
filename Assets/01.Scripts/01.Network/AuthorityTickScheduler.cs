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
    /// RunManager는 phase·타이머 책임만 유지하고, 플레이어·AI·투사체 실행 순서는 여기서 관리한다.
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

        private readonly List<TickCallback> _callbacks = new List<TickCallback>();

        private bool _isSorted  = true;
        private bool _isTicking = false;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버 프로세스의 인스턴스, 클라이언트에서는 null이다 </summary>
        public static AuthorityTickScheduler ServerInstance { get; private set; }

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

            if (!_isSorted)
            {
                _callbacks.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                _isSorted = true;
            }

            for (int i = 0; i < _callbacks.Count; ++i)
            {
                _callbacks[i].Callback.Invoke(Time.fixedDeltaTime);
            }
        }

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            ServerInstance = this;
            _isTicking     = true;
        }

        public override void OnStopServer()
        {
            _isTicking = false;
            _callbacks.Clear();

            if (ServerInstance == this)
            {
                ServerInstance = null;
            }
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 우선순위가 낮을수록 먼저 실행된다. deltaTime은 물리 고정 스텝이 전달된다 </summary>
        [Server]
        public void Register(int priority, Action<float> callback)
        {
            _callbacks.Add(new TickCallback { Priority = priority, Callback = callback });
            _isSorted = false;
        }

        [Server]
        public void Unregister(Action<float> callback)
        {
            for (int i = _callbacks.Count - 1; i >= 0; --i)
            {
                if (_callbacks[i].Callback == callback)
                {
                    _callbacks.RemoveAt(i);
                }
            }
        }
    }
}
