// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Combat;
using Geumeum.Common;

namespace Geumeum.UI
{
    /// <summary>
    /// 파티 플레이트에 누구를 그릴지 정하고 값을 물린다.
    ///
    /// 스폰 목록은 주기로 훑고, 값은 훑지 않는다 — 체력 · 스태미나 · 신력은 PlayerStatus의 이벤트를
    /// 구독해 바뀔 때만 받는다(HUD_DESIGN 4장 3번). 목록만 주기로 만드는 방식은 MinimapPlayerDots와 같다.
    ///
    /// 읽기 전용이다. 여기서 상태를 바꾸지 않고 [Command]도 [Server]도 두지 않는다
    /// </summary>
    public class PartyHudController : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        // 매 프레임 스폰 목록을 다시 훑으면 그 자체가 부하다. 사람이 들고 나는 빈도에는 이걸로 충분하다
        private const float REFRESH_SECONDS = 0.2f;

        [Header("줄")]
        [Tooltip("위에서부터 순서대로. 사람이 모자라면 뒤쪽 줄이 숨는다")]
        [SerializeField] private PartyMemberPlate[] _plates = new PartyMemberPlate[0];

        [Header("표시 대상")]
        [Tooltip("내 줄도 파티창에 넣을지. 끄면 좌하단 자원 바와 겹치지 않는다")]
        [SerializeField] private bool _includeLocalPlayer = false;

        [Tooltip("이름도 직업도 아직 올라오지 않은 동안 쓸 문구")]
        [SerializeField] private string _unknownName = "참가자";

        private readonly List<PlayerStatus> _players = new List<PlayerStatus>(Constants.Player.PARTY_SIZE);

        private Binding[] _bindings = null;
        private float     _nextRefreshTime = 0f;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _bindings = new Binding[_plates.Length];
            for (int i = 0; i < _plates.Length; ++i)
            {
                _bindings[i] = new Binding(_plates[i], _unknownName);

                // 첫 갱신 전까지 빈 줄이 그대로 보이지 않게 먼저 숨긴다.
                // 줄은 이 컨트롤러만 켜고 끄므로 여기서 초기 상태를 정해도 경합하지 않는다
                if (_plates[i] != null)
                {
                    _plates[i].SetVisible(false);
                }
            }
        }

        private void OnEnable()
        {
            _nextRefreshTime = 0f; // 켜자마자 한 번 훑는다. 씬을 다시 들어왔을 때 빈 줄로 남지 않는다
        }

        private void OnDisable()
        {
            UnbindAll();
        }

        private void OnDestroy()
        {
            UnbindAll();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.unscaledTime + REFRESH_SECONDS;
            Refresh();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Refresh()
        {
            if (_bindings == null)
            {
                return;
            }

            CollectPlayers();

            for (int i = 0; i < _bindings.Length; ++i)
            {
                PlayerStatus wanted = (i < _players.Count) ? _players[i] : null;

                _bindings[i].Rebind(wanted);
                _bindings[i].RefreshIdentity(); // 이름과 직업은 SyncVar라 뒤늦게 올라온다
            }
        }

        // 체력 계열이 SyncVar라 클라이언트도 남의 오브젝트를 읽을 수 있다. MinimapPlayerDots와 같은 방식이다
        private void CollectPlayers()
        {
            _players.Clear();

            Dictionary<uint, NetworkIdentity> spawned = (NetworkClient.active)
                ? NetworkClient.spawned
                : NetworkServer.spawned;

            if (spawned == null)
            {
                return;
            }

            foreach (NetworkIdentity identity in spawned.Values)
            {
                if (identity == null)
                {
                    continue;
                }

                if (!identity.TryGetComponent(out PlayerStatus status))
                {
                    continue;
                }

                if ((!_includeLocalPlayer) && (status == PlayerStatus.Local))
                {
                    continue;
                }

                _players.Add(status);
            }

            // 갱신마다 순서가 바뀌면 어느 줄이 누구인지 눈으로 못 따라간다
            _players.Sort(CompareByNetId);
        }

        private static int CompareByNetId(PlayerStatus left, PlayerStatus right)
        {
            return left.netId.CompareTo(right.netId);
        }

        private void UnbindAll()
        {
            if (_bindings == null)
            {
                return;
            }

            for (int i = 0; i < _bindings.Length; ++i)
            {
                _bindings[i].Rebind(null);
            }
        }

        /****************************************
        *                 Types
        ****************************************/

        /// <summary>
        /// 줄 하나와 그 줄이 보고 있는 사람을 묶는다.
        /// 구독과 해제를 1:1로 맞추려고 따로 둔다 — 씬 전환에 남은 구독이 이 프로젝트에서 가장 잦은 UI 결함이다
        /// </summary>
        private sealed class Binding
        {
            private readonly PartyMemberPlate _plate;
            private readonly string           _unknownName;

            private PlayerStatus    _status    = null;
            private PlayerNameTag   _nameTag   = null;
            private PlayerCharacter _character = null;

            public Binding(PartyMemberPlate plate, string unknownName)
            {
                _plate       = plate;
                _unknownName = unknownName;
            }

            public void Rebind(PlayerStatus status)
            {
                // 접속이 끊기면 오브젝트가 파괴된다. ==로 비교하면 파괴된 대상이 null과 같아져
                // 줄을 숨기지 못한 채 나간 사람의 값이 남는다
                if (ReferenceEquals(_status, status))
                {
                    return;
                }

                Unbind();

                if (status == null)
                {
                    return;
                }

                _status = status;
                status.TryGetComponent(out _nameTag);   // 없으면 null. 이름은 대체 문구로 채운다
                status.TryGetComponent(out _character);

                _status.HealthChanged    += OnHealthChanged;
                _status.StaminaChanged   += OnStaminaChanged;
                _status.SpiritChanged    += OnSpiritChanged;
                _status.LifeStateChanged += OnLifeStateChanged;

                if (_plate != null)
                {
                    _plate.SetVisible(true);
                }

                _status.RaiseAll(); // 뒤늦게 구독했으니 지금 값을 한 번 받아온다
            }

            /// <summary> 이름과 직업은 로비에서 배정되어 뒤늦게 올라온다. 값과 달리 이벤트가 없어 여기서 다시 읽는다 </summary>
            public void RefreshIdentity()
            {
                if ((_status == null) || (_plate == null))
                {
                    return;
                }

                bool          hasCharacter = (_character != null) && _character.HasCharacter;
                CharacterKind character    = hasCharacter ? _character.Character : default;

                _plate.SetCharacter(hasCharacter, character);
                _plate.SetName(ResolveName(hasCharacter, character));
            }

            private string ResolveName(bool hasCharacter, CharacterKind character)
            {
                if ((_nameTag != null) && (!string.IsNullOrEmpty(_nameTag.DisplayName)))
                {
                    return _nameTag.DisplayName;
                }

                // 이름이 아직 안 올라왔거나 KCP 개발 경로다. 직업으로라도 구분한다
                return hasCharacter ? FunctionalUiText.GetCharacterName(character) : _unknownName;
            }

            private void Unbind()
            {
                if (_status != null)
                {
                    _status.HealthChanged    -= OnHealthChanged;
                    _status.StaminaChanged   -= OnStaminaChanged;
                    _status.SpiritChanged    -= OnSpiritChanged;
                    _status.LifeStateChanged -= OnLifeStateChanged;
                }

                _status    = null;
                _nameTag   = null;
                _character = null;

                if (_plate != null)
                {
                    _plate.SetDowned(false);
                    _plate.SetVisible(false);
                }
            }

            private void OnHealthChanged(float current, float max)
            {
                if (_plate != null)
                {
                    _plate.SetHealth(current, max);
                }
            }

            private void OnStaminaChanged(float current, float max)
            {
                if (_plate != null)
                {
                    _plate.SetStamina(current, max);
                }
            }

            private void OnSpiritChanged(float current, float max)
            {
                if (_plate != null)
                {
                    _plate.SetSpirit(current, max);
                }
            }

            private void OnLifeStateChanged(PlayerLifeState state)
            {
                if (_plate == null)
                {
                    return;
                }

                _plate.SetDowned((state == PlayerLifeState.Downed) || (state == PlayerLifeState.Spectating));
            }
        }
    }
}
