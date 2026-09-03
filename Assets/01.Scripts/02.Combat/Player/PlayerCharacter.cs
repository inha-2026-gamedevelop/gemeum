// System
using System;

// Unity
using UnityEngine;
using UnityEngine.Serialization;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary>
    /// 로비에서 고른 캐릭터의 모델을 켠다. 서버가 확정하고 클라이언트는 읽기만 하며,
    /// 판정에는 관여하지 않는 표현 전용 컴포넌트다
    /// </summary>
    public class PlayerCharacter : NetworkBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        private struct CharacterModelBinding
        {
            // FormerlySerializedAs는 이름을 바꾸기 전 Appearance로 저장된 프리팹 값을 잇는다
            [FormerlySerializedAs("Appearance")]
            public CharacterKind Character;
            public GameObject    Model;
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private CharacterModelBinding[] _bindings = Array.Empty<CharacterModelBinding>();

        [SyncVar(hook = nameof(OnCharacterChanged))]    private CharacterKind _character    = default;
        [SyncVar(hook = nameof(OnHasCharacterChanged))] private bool          _hasCharacter = false;

        private PlayerAnimationController _animation;
        private PlayerWeaponVisual        _weaponVisual;

        private CharacterKind _appliedCharacter    = default;
        private bool          _hasAppliedCharacter = false;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 런 시작 전에는 캐릭터가 확정되지 않는다, 기본값을 선택 결과로 읽지 않도록 이 값을 먼저 본다 </summary>
        public bool HasCharacter => _hasCharacter;

        public CharacterKind Character => _character;

        /// <summary> 지금 켜져 있는 캐릭터 모델 루트. 무기 시각화가 오른손 뼈를 찾을 때 쓴다 </summary>
        public GameObject ActiveModel { get; private set; }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _animation    = GetComponent<PlayerAnimationController>();
            _weaponVisual = GetComponent<PlayerWeaponVisual>();
        }

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        /// <summary> 서버, 클라이언트 모두에서 값이 바뀔 때 호출된다, 판정에는 관여하지 않는다 </summary>
        private void OnCharacterChanged(CharacterKind oldCharacter, CharacterKind newCharacter)
        {
            RefreshCharacter();
        }

        // Chasa가 enum 기본값이라 차사를 배정받으면 _character 값이 바뀌지 않아 위 훅이 호출되지 않는다.
        // 확정 여부는 반드시 false에서 true로 바뀌므로 모델 적용을 여기에도 건다.
        // _character가 먼저 선언되어 있어 스폰 payload를 읽을 때 이 시점의 _character는 이미 확정된 값이다
        private void OnHasCharacterChanged(bool oldHasCharacter, bool newHasCharacter)
        {
            RefreshCharacter();
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary>
        /// 런 시작 시 확정된 캐릭터를 적용한다. LobbyController만 호출한다.
        /// 이미 확정된 뒤의 재호출은 거부한다, 런 도중 캐릭터가 바뀌면 파티원 식별이 흔들린다
        /// </summary>
        [Server]
        public bool ServerSetCharacter(CharacterKind character)
        {
            if (_hasCharacter)
            {
                Debug.LogWarning($"[PlayerCharacter] 이미 캐릭터가 확정된 플레이어에 다시 적용하려 했다: {_character}", this);
                return false;
            }

            _character    = character;
            _hasCharacter = true;
            return true;
        }

        /// <summary>
        /// 런이 끝나 로비로 돌아갈 때 확정을 되돌린다. LobbyController만 호출한다.
        /// RefreshCharacter는 확정되지 않은 상태에서 아무것도 하지 않으므로 모델은 그대로 남는다,
        /// 로비에서 캐릭터가 사라지지 않게 하려는 의도다
        /// </summary>
        [Server]
        public void ServerClearForNewRun()
        {
            _hasCharacter = false;
        }

        /****************************************
        *                Methods
        ****************************************/

        private void RefreshCharacter()
        {
            if (!_hasCharacter)
            {
                return;
            }

            ApplyCharacter(_character);
        }

        /// <summary>
        /// 캐릭터에 매칭되는 모델만 켠다. 매칭되는 모델이 없으면 현재 모델을 보존한다.
        /// 새 모델이 준비되지 않은 상태에서 기존 모델부터 끄면 플레이어가 통째로 사라지기 때문이다
        /// </summary>
        private void ApplyCharacter(CharacterKind character)
        {
            // 캐릭터와 확정 여부 두 SyncVar가 각각 훅을 걸고 있어 같은 캐릭터로 두 번 들어올 수 있다
            if ((_hasAppliedCharacter) && (_appliedCharacter == character))
            {
                return;
            }

            GameObject matchedModel = null;

            for (int i = 0; i < _bindings.Length; ++i)
            {
                CharacterModelBinding binding = _bindings[i];

                if ((binding.Character != character) || (binding.Model == null))
                {
                    continue;
                }

                matchedModel = binding.Model;
                break;
            }

            if (matchedModel == null)
            {
                Debug.LogWarning($"[PlayerCharacter] {character}에 매칭되는 모델 바인딩이 없어 현재 모델을 유지한다", this);
                return;
            }

            // 사용할 모델을 먼저 확정한 뒤 한 번에 전환한다. 같은 모델을 여러 캐릭터의 임시 모델로
            // 공유해도 마지막 바인딩 순서에 따라 다시 꺼지지 않는다
            for (int i = 0; i < _bindings.Length; ++i)
            {
                GameObject model = _bindings[i].Model;
                if (model != null)
                {
                    model.SetActive(model == matchedModel);
                }
            }

            _appliedCharacter    = character;
            _hasAppliedCharacter = true;
            ActiveModel          = matchedModel;

            if (_animation != null)
            {
                _animation.RefreshAnimator();
            }

            if (_weaponVisual != null)
            {
                _weaponVisual.Refresh();
            }
        }
    }
}
