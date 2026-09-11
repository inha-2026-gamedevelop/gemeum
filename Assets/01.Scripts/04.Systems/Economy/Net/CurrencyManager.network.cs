// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    public partial class CurrencyManager
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar(hook = nameof(OnBalanceChanged))] private int  _balance         = 0;
        [SyncVar]                                      private uint _balanceRevision = 0u;

        /****************************************
        *           Client Callbacks
        ****************************************/

        public override void OnStartClient()
        {
            PartyWalletLocator.Register(this);
        }

        public override void OnStopClient()
        {
            PartyWalletLocator.Unregister(this);
        }

        /****************************************
        *             SyncVar Hooks
        ****************************************/

        private void OnBalanceChanged(int oldValue, int newValue)
        {
            Action<int> handlers = BalanceChanged;
            if (handlers == null)
            {
                return;
            }

            Delegate[] observers = handlers.GetInvocationList();
            for (int i = 0; i < observers.Length; ++i)
            {
                try
                {
                    ((Action<int>) observers[i])(newValue);
                }
                catch (Exception e)
                {
                    // SyncVar hook 밖으로 예외가 나가면 Mirror hook guard도 복구되지 않는다.
                    Debug.LogError($"[CurrencyManager] 잔액 observer 실패: {e}", this);
                }
            }
        }
    }
}
