// Unity
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.UI
{
    /// <summary>
    /// 파티 플레이트 한 줄. 이름 · 직업 아이콘 · 체력 · 스태미나 · 신력을 그린다.
    ///
    /// 이 컴포넌트는 값만 받는다 — PlayerStatus도 네트워크도 모른다(HUD_DESIGN 4장 2번).
    /// 누구의 값인지는 <see cref="PartyHudController"/>가 정해서 넣어 준다
    /// </summary>
    public class PartyMemberPlate : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("줄 전체")]
        [Tooltip("사람이 없는 줄을 숨기고 쓰러진 줄을 흐리게 하는 데 쓴다")]
        [SerializeField] private CanvasGroup _group = null;

        [Header("표시")]
        [SerializeField] private TMP_Text _nameLabel = null;
        [SerializeField] private Image    _icon      = null;

        [Header("자원 바 채움")]
        [SerializeField] private Image _healthFill  = null;
        [SerializeField] private Image _staminaFill = null;
        [SerializeField] private Image _spiritFill  = null;

        [Header("직업 아이콘")]
        [SerializeField] private Sprite _chasaIcon   = null;
        [SerializeField] private Sprite _singungIcon = null;
        [SerializeField] private Sprite _munyeoIcon  = null;

        [Header("쓰러진 파티원")]
        [Tooltip("쓰러졌을 때 줄 전체의 불투명도. 숨기지 않는 이유는 누가 쓰러졌는지도 정보이기 때문이다")]
        [SerializeField, Range(0.1f, 1f)] private float _downedAlpha = 0.45f;

        private bool _isVisible = true;
        private bool _isDowned  = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_group == null)
            {
                _group = GetComponent<CanvasGroup>();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이 줄에 사람이 있는지. 비면 통째로 숨긴다 </summary>
        public void SetVisible(bool isVisible)
        {
            _isVisible = isVisible;
            ApplyAlpha();
        }

        public void SetName(string displayName)
        {
            if (_nameLabel == null)
            {
                return;
            }

            if (_nameLabel.text != displayName)
            {
                _nameLabel.text = displayName;
            }
        }

        /// <summary> 직업이 아직 배정되지 않았으면 hasCharacter가 false다. 그때는 아이콘을 비운다 </summary>
        public void SetCharacter(bool hasCharacter, CharacterKind character)
        {
            if (_icon == null)
            {
                return;
            }

            Sprite wanted = hasCharacter ? ResolveIcon(character) : null;
            if (_icon.sprite != wanted)
            {
                _icon.sprite = wanted;
            }

            _icon.enabled = (wanted != null);
        }

        public void SetHealth(float current, float max)  => SetFill(_healthFill, current, max);
        public void SetStamina(float current, float max) => SetFill(_staminaFill, current, max);
        public void SetSpirit(float current, float max)  => SetFill(_spiritFill, current, max);

        /// <summary> 쓰러졌거나 관전 중이다. 치유와 부활을 판단하는 정보라 숨기지 않고 흐리게만 한다 </summary>
        public void SetDowned(bool isDowned)
        {
            _isDowned = isDowned;
            ApplyAlpha();
        }

        private void ApplyAlpha()
        {
            if (_group == null)
            {
                return;
            }

            float wanted = !_isVisible ? 0f : (_isDowned ? _downedAlpha : 1f);
            if (!Mathf.Approximately(_group.alpha, wanted))
            {
                _group.alpha = wanted;
            }
        }

        private Sprite ResolveIcon(CharacterKind character)
        {
            switch (character)
            {
                case CharacterKind.Chasa:
                    return _chasaIcon;
                case CharacterKind.Singung:
                    return _singungIcon;
                case CharacterKind.Munyeo:
                    return _munyeoIcon;
                default:
                    return null;
            }
        }

        private static void SetFill(Image image, float current, float max)
        {
            if (image == null)
            {
                return;
            }

            image.fillAmount = (max > 0f) ? Mathf.Clamp01(current / max) : 0f;
        }
    }
}
