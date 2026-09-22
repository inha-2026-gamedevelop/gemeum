using Geumeum.Common;
using Geumeum.Systems;
using Mirror;
using TMPro;
using UnityEngine;

namespace Geumeum.UI
{
    /// <summary> 로컬 상호작용 후보와 서버가 확정한 획득/거절 결과를 표시한다 </summary>
    public sealed class InteractionPromptView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;
        [SerializeField] private GameObject _root;
        [SerializeField] private float _feedbackSeconds = 3f;
        private NetworkIdentity _owner;
        private InteractionController _interaction;
        private uint _candidateId;
        private DroppedItem _drop;
        private string _feedback;
        private float _feedbackUntil;
        public string DisplayText => _label == null ? string.Empty : _label.text;

        private void Update()
        {
            if (_owner != NetworkClient.localPlayer)
            {
                Unbind();
                _owner = NetworkClient.localPlayer;
                _interaction = _owner == null ? null : _owner.GetComponent<InteractionController>();
                if (_interaction != null) { _interaction.InteractionCompleted += OnCompleted; }
            }
            if (_interaction == null || !LocalGameplayInputGate.AllowsGameplayInput)
            { Show(string.Empty); return; }
            if (Time.unscaledTime < _feedbackUntil) { Show(_feedback); return; }
            if (_candidateId != _interaction.CandidateNetId)
            {
                _candidateId = _interaction.CandidateNetId;
                _drop = NetworkClient.spawned.TryGetValue(_candidateId, out var candidate)
                    ? candidate.GetComponent<DroppedItem>() : null;
            }
            string prompt = _interaction.PromptText;
            if (_drop != null)
            {
                var equipment = _drop.Equipment;
                prompt = FunctionalUiText.GetGradeName(equipment.Grade) + " " +
                    FunctionalUiText.GetWeaponName(equipment.WeaponKind) + " 줍기";
            }
            Show(string.IsNullOrEmpty(prompt) ? string.Empty : "[" + _interaction.InteractKey + "] " + prompt);
        }

        private void OnCompleted(InteractionResult result, InventoryResult inventory, WeaponCombatSnapshot equipment)
        {
            _feedback = FormatResult(result, inventory, equipment);
            _feedbackUntil = Time.unscaledTime + _feedbackSeconds;
        }

        public static string FormatResult(InteractionResult result, InventoryResult inventory, WeaponCombatSnapshot equipment)
        {
            switch (result)
            {
                case InteractionResult.TooFar: return "조금 더 가까이에서 이용해 주세요.";
                case InteractionResult.InCombat: return "전투가 끝난 뒤 잠시 기다려 주세요.";
                case InteractionResult.Dead: return "쓰러진 동안에는 이용할 수 없습니다.";
                case InteractionResult.RunNotActive: return "지금은 이용할 수 없습니다.";
                case InteractionResult.WrongScene:
                case InteractionResult.Unavailable:
                case InteractionResult.InvalidRequest: return "이미 가져갔거나 이용할 수 없는 대상입니다.";
            }
            if (inventory != InventoryResult.Success) { return FunctionalUiText.GetInventoryResult(inventory); }
            return FunctionalUiText.GetGradeName(equipment.Grade) + " " +
                FunctionalUiText.GetWeaponName(equipment.WeaponKind) + "을(를) 배낭에 넣었습니다. [I] 배낭";
        }

        private void Show(string text)
        {
            if (_label != null && _label.text != text) { _label.text = text; }
            bool visible = !string.IsNullOrEmpty(text);
            if (_root != null && _root.activeSelf != visible) { _root.SetActive(visible); }
        }
        private void Unbind()
        {
            if (_interaction != null) { _interaction.InteractionCompleted -= OnCompleted; }
            _interaction = null; _owner = null; _drop = null; _candidateId = 0u;
            _feedback = null; _feedbackUntil = 0f;
        }
        private void OnDisable() { Unbind(); Show(string.Empty); }
    }
}
