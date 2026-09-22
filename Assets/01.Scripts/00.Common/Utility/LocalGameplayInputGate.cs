// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 로컬 모달이 게임 입력과 커서를 중첩 안전하게 소유하는 토큰 </summary>
    public sealed class LocalGameplayInputToken : IDisposable
    {
        private readonly uint _generation;
        private readonly uint _tokenId;
        private bool          _isDisposed;

        internal LocalGameplayInputToken(uint generation, uint tokenId)
        {
            _generation = generation;
            _tokenId     = tokenId;
        }

        internal bool IsActive => (!_isDisposed) &&
            LocalGameplayInputGate.Contains(_generation, _tokenId);

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            LocalGameplayInputGate.Release(_generation, _tokenId);
        }
    }

    /// <summary> 카메라 ESC 상태와 별개로 로컬 플레이 입력을 막는 공용 소유권 </summary>
    public static class LocalGameplayInputGate
    {
        private static readonly HashSet<uint> G_ActiveTokens = new HashSet<uint>();

        private static uint           G_Generation = 1u;
        private static uint           G_NextTokenId = 1u;
        private static bool           G_PreviousCameraInput;
        private static CursorLockMode G_PreviousCursorLock;
        private static bool           G_PreviousCursorVisible;

        public static bool AllowsGameplayInput => (G_ActiveTokens.Count == 0);

        public static int ActiveTokenCount => G_ActiveTokens.Count;

        /// <summary> 모달 입력 소유권을 하나 얻는다, 반환 토큰은 반드시 Dispose한다 </summary>
        public static LocalGameplayInputToken Acquire()
        {
            // 설치 조준 이전 상태로 복구한 뒤 모달의 복구값을 저장한다
            LocalPlacementAim.End();
            uint tokenId = TakeTokenId();
            if (G_ActiveTokens.Count == 0)
            {
                G_PreviousCameraInput   = CameraFrame.InputEnabled;
                G_PreviousCursorLock    = Cursor.lockState;
                G_PreviousCursorVisible = Cursor.visible;
            }

            G_ActiveTokens.Add(tokenId);
            ApplyModalState();
            return new LocalGameplayInputToken(G_Generation, tokenId);
        }

        /// <summary> CameraRig의 ESC 처리 뒤에도 활성 모달의 커서 상태를 유지한다 </summary>
        public static void Maintain(LocalGameplayInputToken token)
        {
            if ((token == null) || (!token.IsActive))
            {
                return;
            }

            ApplyModalState();
        }

        internal static bool Contains(uint generation, uint tokenId)
        {
            return (generation == G_Generation) && G_ActiveTokens.Contains(tokenId);
        }

        internal static void Release(uint generation, uint tokenId)
        {
            if ((generation != G_Generation) || (!G_ActiveTokens.Remove(tokenId)) ||
                (G_ActiveTokens.Count != 0))
            {
                return;
            }

            CameraFrame.InputEnabled = G_PreviousCameraInput;
            Cursor.lockState         = G_PreviousCursorLock;
            Cursor.visible           = G_PreviousCursorVisible;
        }

        private static uint TakeTokenId()
        {
            uint tokenId = G_NextTokenId;
            ++G_NextTokenId;
            if (G_NextTokenId == 0u)
            {
                G_NextTokenId = 1u;
            }

            while ((tokenId == 0u) || G_ActiveTokens.Contains(tokenId))
            {
                tokenId = G_NextTokenId;
                ++G_NextTokenId;
                if (G_NextTokenId == 0u)
                {
                    G_NextTokenId = 1u;
                }
            }

            return tokenId;
        }

        private static void ApplyModalState()
        {
            CameraFrame.InputEnabled = false;
            Cursor.lockState         = CursorLockMode.None;
            Cursor.visible           = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            G_ActiveTokens.Clear();
            ++G_Generation;
            if (G_Generation == 0u)
            {
                G_Generation = 1u;
            }

            G_NextTokenId = 1u;
        }
    }
}
