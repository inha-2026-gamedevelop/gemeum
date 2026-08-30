// System
using System;
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary>
    /// 서버 전용 공용 시뮬레이션 틱. 등록된 콜백을 우선순위 순서로 매 틱 실행한다.
    /// RunManager는 phase·타이머 책임만 유지하고, 플레이어·AI·투사체 실행 순서는 여기서 관리한다
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

        private bool            _isSorted    = true;
        private Coroutine       _tickRoutine = null;
        private WaitForSeconds  _tickWait    = null;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버 프로세스의 인스턴스, 클라이언트에서는 null이다 </summary>
        public static AuthorityTickScheduler ServerInstance { get; private set; }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _tickWait = new WaitForSeconds(Constants.Network.SERVER_TICK_INTERVAL);
        }

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            ServerInstance = this;
            _tickRoutine    = StartCoroutine(CoServerTick());
        }

        public override void OnStopServer()
        {
            if (_tickRoutine != null)
            {
                StopCoroutine(_tickRoutine);
                _tickRoutine = null;
            }

            if (ServerInstance == this)
            {
                ServerInstance = null;
            }
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 우선순위가 낮을수록 먼저 실행된다. deltaTime은 SERVER_TICK_INTERVAL 고정값이 전달된다 </summary>
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

        /****************************************
        *                Methods
        ****************************************/

        private IEnumerator CoServerTick()
        {
            while (true)
            {
                if (!_isSorted)
                {
                    _callbacks.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                    _isSorted = true;
                }

                for (int i = 0; i < _callbacks.Count; ++i)
                {
                    _callbacks[i].Callback.Invoke(Constants.Network.SERVER_TICK_INTERVAL);
                }

                yield return _tickWait;
            }
        }
    }
}
