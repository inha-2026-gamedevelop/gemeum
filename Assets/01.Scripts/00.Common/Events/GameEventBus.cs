// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// @Managers가 소유하는 이벤트 허브
    /// 잡귀 사망처럼 파트 경계를 넘는 통지는 상대 파트의 클래스를 직접 부르지 않고 전부 여기를 지난다
    /// </summary>
    public class GameEventBus : SceneSingleton<GameEventBus>, IGameEventBus
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Dictionary<Type, Delegate> _handlers = new Dictionary<Type, Delegate>();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이벤트를 발행한다, 구독자가 없으면 아무 일도 하지 않는다 </summary>
        public void Publish<T>(in T payload) where T : struct
        {
            if (!_handlers.TryGetValue(typeof(T), out Delegate registered))
            {
                return;
            }

            Delegate[] observers = registered.GetInvocationList();
            for (int i = 0; i < observers.Length; ++i)
            {
                try
                {
                    ((Action<T>) observers[i])(payload);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[GameEventBus] {typeof(T).Name} observer 실패: {e}", this);
                }
            }
        }

        /// <summary> 구독한다, 같은 핸들러를 두 번 넣으면 두 번 불린다 </summary>
        public void Subscribe<T>(Action<T> handler) where T : struct
        {
            if (handler == null)
            {
                Debug.LogError($"[GameEventBus] null 핸들러를 구독하려 했다: {typeof(T).Name}", this);
                return;
            }

            Type key = typeof(T);
            _handlers.TryGetValue(key, out Delegate registered);
            _handlers[key] = (Action<T>) registered + handler;
        }

        /// <summary> 구독을 해제한다 </summary>
        public void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            if (handler == null)
            {
                return;
            }

            Type key = typeof(T);
            if (!_handlers.TryGetValue(key, out Delegate registered))
            {
                return;
            }

            Action<T> remaining = (Action<T>) registered - handler;
            if (remaining == null)
            {
                _handlers.Remove(key);
                return;
            }

            _handlers[key] = remaining;
        }

        /****************************************
        *              Singleton
        ****************************************/

        protected override void OnSingletonDestroy()
        {
            _handlers.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSingletonStatic()
        {
            ResetStatic();
        }
    }
}
