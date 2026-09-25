// System
using System;
using System.Collections;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Systems
{
    /// <summary> 객주의 서버 위치와 상점 UI 진입 상호작용을 소유한다 </summary>
    [DisallowMultipleComponent]
    public partial class ShopInteractionPoint : NetworkBehaviour, IInteractable
    {
        private Coroutine _eventBusWaitRoutine;
        private bool      _hasEventSubscription;

        public string PromptText => "객주 이용";

        /// <summary> 문을 열기 전에 곁에 오면 띄우는 안내. 누를 수 있는 것이 없으니 키를 붙이지 않는다 </summary>
        public string ClosedNoticeText => "객주 · 두 번째 결계가 좁혀지면 문을 엽니다";

        public bool IsOpen => _isOpen;

        /// <summary> 소유 클라이언트가 서버 승인을 받아 상점을 열었다 </summary>
        public event Action<uint> OpenRequested;

        public bool CanInteract(uint playerNetId)
        {
            if (!isServer)
            {
                return _isOpen;
            }

            return (ServerEvaluate(playerNetId) == InteractionResult.Success);
        }

        [TargetRpc]
        private void TargetOpenShop(NetworkConnection target, uint shopNetId)
        {
            OpenRequested?.Invoke(shopNetId);
        }
    }
}
