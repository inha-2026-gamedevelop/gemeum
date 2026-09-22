// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>Invokes every observer even when one subscriber fails</summary>
    public static class ObserverDispatch
    {
        public static void Invoke<T>(Action<T> handlers, T value,
            UnityEngine.Object context, string owner, string signal)
        {
            if (handlers == null)
            {
                return;
            }

            Delegate[] observers = handlers.GetInvocationList();
            for (int i = 0; i < observers.Length; ++i)
            {
                try
                {
                    ((Action<T>) observers[i])(value);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[{owner}] {signal} observer failed: {exception}", context);
                }
            }
        }
    }
}
