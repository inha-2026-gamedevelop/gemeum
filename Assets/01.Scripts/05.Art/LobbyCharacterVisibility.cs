// System
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Combat;
using Geumeum.Common;
using Geumeum.Network;

namespace Geumeum.Art
{
    /// <summary>
    /// 로비 단계에서 플레이어 캐릭터를 화면에서 감춘다
    ///
    /// 스폰과 권위 흐름은 건드리지 않는다 - 서버는 평소대로 플레이어를 만들고,
    /// 각 클라이언트가 자기 화면에서 renderer만 끈다. 보이는 것만 바뀌므로
    /// 사람마다 게임 결과가 달라질 여지가 없다
    ///
    /// 껐던 renderer만 골라 되켠다. 원래 꺼져 있던 것까지 켜면 로비를 한 번 지날 때마다
    /// 숨겨 둔 부위가 드러난다
    /// </summary>
    public class LobbyCharacterVisibility : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        // 로비에 사람이 드나드는 주기라 매 프레임 훑을 이유가 없다
        // 런 단계는 클라이언트에 구독할 이벤트가 없어 여기서만 확인한다
        private const float SCAN_INTERVAL_SECONDS = 0.25f;

        /****************************************
        *                Fields
        ****************************************/

        // 이 컴포넌트가 직접 끈 것만 담는다
        private readonly List<Renderer> _hidden = new List<Renderer>();

        private readonly List<Renderer> _buffer = new List<Renderer>();

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            StartCoroutine(CoTrackLobbyPhase());
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            RevealAll();
        }

        /****************************************
        *                Methods
        ****************************************/

        private IEnumerator CoTrackLobbyPhase()
        {
            var wait = new WaitForSeconds(SCAN_INTERVAL_SECONDS);

            while (true)
            {
                yield return wait;

                if (IsLobbyPhase())
                {
                    HideSpawnedCharacters();
                    continue;
                }

                // 런이 시작되면 되돌린다. 다음 로비까지는 훑을 이유가 없다
                if (_hidden.Count > 0)
                {
                    RevealAll();
                }
            }
        }

        private static bool IsLobbyPhase()
        {
            if (!NetworkClient.active)
            {
                return false;
            }

            RunManager run = RunManager.LocalInstance;
            return ((run != null) && (run.Phase == RunPhase.Lobby));
        }

        private void HideSpawnedCharacters()
        {
            foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
            {
                if ((identity == null) || (identity.GetComponent<PlayerRunAgent>() == null))
                {
                    continue;
                }

                _buffer.Clear();
                identity.GetComponentsInChildren(true, _buffer);

                for (int i = 0; i < _buffer.Count; ++i)
                {
                    Renderer target = _buffer[i];
                    if ((target == null) || (!target.enabled))
                    {
                        continue;
                    }

                    target.enabled = false;
                    _hidden.Add(target);
                }
            }
        }

        private void RevealAll()
        {
            for (int i = 0; i < _hidden.Count; ++i)
            {
                Renderer target = _hidden[i];
                if (target != null)
                {
                    target.enabled = true;
                }
            }

            _hidden.Clear();
        }
    }
}
