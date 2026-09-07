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
    /// <summary> 로비에서 고른 캐릭터의 모델을 켠다 </summary>
    public class PlayerCharacter : NetworkBehaviour, IRunStateCarrier
    {
        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        private struct CharacterModelBinding
        {
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

        /// <summary> 런 시작 전에는 캐릭터가 확정되지 않는다 </summary>
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

        private void OnCharacterChanged(CharacterKind oldCharacter, CharacterKind newCharacter)
        {
            RefreshCharacter();
        }

        private void OnHasCharacterChanged(bool oldHasCharacter, bool newHasCharacter)
        {
            RefreshCharacter();
        }

        /****************************************
        *           Interface Members
        ****************************************/

        public int CarrierOrder => 10;

        /// <summary> 확정된 캐릭터를 담는다 </summary>
        public void Capture(RunParticipantSnapshot snapshot)
        {
            if (!_hasCharacter)
            {
                return;
            }

            snapshot.Character = _character;
        }

        public bool Validate(RunParticipantSnapshot snapshot, out string failure)
        {
            if (!Enum.IsDefined(typeof(CharacterKind), snapshot.Character))
            {
                failure = $"알 수 없는 캐릭터다: {snapshot.Character}";
                return false;
            }

            failure = null;
            return true;
        }

        /// <summary> 담아 둔 캐릭터를 되붓는다 </summary>
        public bool Restore(RunParticipantSnapshot snapshot)
        {
            _character    = snapshot.Character;
            _hasCharacter = true;

            RefreshCharacter();
            return true;
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 런 시작 시 확정된 캐릭터를 적용한다 </summary>
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

        /// <summary> 런이 끝나 로비로 돌아갈 때 확정을 되돌린다 </summary>
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

        /// <summary> 캐릭터에 매칭되는 모델만 켠다 </summary>
        private void ApplyCharacter(CharacterKind character)
        {
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
