// Unity
using UnityEngine;

// 서드파티
using Mirror;
using TMPro;

// 프로젝트 내부
using Geumeum.Combat;
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;
using Geumeum.Systems;

namespace Geumeum.UI
{
    /// <summary>
    /// 파티원 곁에 서면 양도 안내를 띄운다
    ///
    /// 이 뷰는 서버에 아무 요청도 보내지 않는다. G를 받으면 배낭(FunctionalManagementPanel)을
    /// 양도 모드로 열어 줄 뿐이고, 실제 양도는 거기서 EquipmentTransferService로 나간다
    ///
    /// 줍기·객주와 같은 G를 쓰므로, 그쪽 후보가 잡혀 있으면 이 안내는 비킨다.
    /// 한 키에 두 문구가 겹쳐 보이면 무엇이 눌리는지 알 수 없다
    /// </summary>
    public sealed class AllyTransferPromptView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        // 사람이 걸어서 드나드는 빈도에는 이걸로 충분하다. PartyHudController와 같은 주기다
        private const float REFRESH_SECONDS = 0.2f;

        [Header("표시")]
        [Tooltip("안내 문구를 그릴 라벨")]
        [SerializeField] private TMP_Text _label;

        [Tooltip("안내가 없을 때 통째로 끌 오브젝트. 비워 두면 라벨만 비운다")]
        [SerializeField] private GameObject _root;

        [Tooltip("이름도 직업도 아직 올라오지 않은 동안 쓸 문구")]
        [SerializeField] private string _unknownName = "참가자";

        [Header("따라붙기")]
        [Tooltip("상대 발밑에서 이만큼 위에 붙인다(m). 머리 위로 올려야 몸에 가리지 않는다")]
        [SerializeField] private float _headHeight = 2.1f;

        [Tooltip("따라붙는 데 걸리는 시간(초). 0이면 딱 붙어 따라다녀 화면이 떨린다")]
        [SerializeField] private float _followSmoothTime = 0.08f;

        private float        _nextRefreshTime = 0f;
        private PlayerStatus _target          = null;

        private RectTransform _promptRect   = null;
        private RectTransform _parentRect   = null;
        private Camera        _camera       = null;
        private Vector2       _followVelocity = Vector2.zero;
        private bool          _hasFollowPoint = false;

        /// <summary> 검증과 테스트에서 읽는다 </summary>
        public string DisplayText => (_label == null) ? string.Empty : _label.text;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            // FixedAspectViewport가 실행 중에 부모를 AspectContent로 바꾼다. 켜질 때 다시 잡는다
            _promptRect = (_root != null) ? (_root.transform as RectTransform) : null;
        }

        private void OnEnable()
        {
            _nextRefreshTime = 0f; // 켜자마자 한 번 훑는다. 씬을 다시 들어왔을 때 빈 채로 남지 않는다
            _hasFollowPoint  = false;
            _promptRect      = (_root != null) ? (_root.transform as RectTransform) : null;
            _parentRect      = (_promptRect != null) ? (_promptRect.parent as RectTransform) : null;
        }

        private void OnDisable()
        {
            _target         = null;
            _hasFollowPoint = false;
            Show(string.Empty);
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextRefreshTime)
            {
                _nextRefreshTime      = Time.unscaledTime + REFRESH_SECONDS;
                PlayerStatus previous = _target;
                _target               = FindNearestAlly();

                // 다른 사람으로 넘어갔으면 그 사람 자리에서 새로 시작한다
                if (!ReferenceEquals(previous, _target))
                {
                    _hasFollowPoint = false;
                }
            }

            // 대상이 접속을 끊으면 오브젝트가 파괴된다. 다음 갱신까지 죽은 참조를 들고 있지 않는다
            if (_target == null)
            {
                _hasFollowPoint = false;
                Show(string.Empty);
                return;
            }

            // 상대가 화면 밖이나 카메라 뒤로 가면 붙일 자리가 없다. 문구도 같이 내린다
            if (!TryFollowTarget())
            {
                _hasFollowPoint = false;
                Show(string.Empty);
                return;
            }

            Show("양도 [" + ResolveInteractKey() + "]");

            // 안내가 떠 있는 동안에만 받는다. 줍기·객주 후보가 있을 때는 이 줄까지 오지 않는다
            if (Input.GetKeyDown(ResolveInteractKeyCode()))
            {
                OpenTransferWindow();
            }
        }

        private void OpenTransferWindow()
        {
            // 새 창을 따로 두지 않는다. 늘 쓰던 배낭이 슬라이드로 열리고 거기에 양도 상자가 붙는다
            FunctionalManagementPanel panel = FunctionalManagementPanel.Instance;
            if ((panel == null) || (panel.IsVisible))
            {
                return;
            }

            panel.OpenTransferTo(_target.netId, ResolveName(_target));

            // 창이 열리면 입력이 잠기므로 안내를 바로 내린다
            _target         = null;
            _hasFollowPoint = false;
            Show(string.Empty);
        }

        /// <summary> 상대 머리 위를 따라간다. 붙일 자리를 못 찾으면 false </summary>
        private bool TryFollowTarget()
        {
            if (_promptRect == null)
            {
                return false;
            }

            if ((_camera == null) || (!_camera.isActiveAndEnabled))
            {
                _camera = Camera.main;
            }

            // FixedAspectViewport가 실행 중에 AspectContent 밑으로 옮긴다. 매번 지금 부모를 본다
            _parentRect = _promptRect.parent as RectTransform;
            if ((_camera == null) || (_parentRect == null))
            {
                return false;
            }

            Vector3 world  = _target.transform.position + (Vector3.up * _headHeight);
            Vector3 screen = _camera.WorldToScreenPoint(world);

            // 카메라 뒤로 넘어간 대상은 화면 좌표가 반대편으로 뒤집혀 엉뚱한 자리에 찍힌다
            if (screen.z <= 0f)
            {
                return false;
            }

            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_parentRect, screen, null, out local))
            {
                return false;
            }

            // 새로 잡은 대상에게 화면을 가로질러 날아가지 않게 첫 프레임은 그 자리에 놓는다
            if (!_hasFollowPoint)
            {
                _promptRect.anchoredPosition = local;
                _followVelocity              = Vector2.zero;
                _hasFollowPoint              = true;
                return true;
            }

            // 딱 붙이지 않고 조금 늦게 따라간다. 상대가 뛰면 그대로 붙은 문구가 눈에 거슬린다
            _promptRect.anchoredPosition = Vector2.SmoothDamp(_promptRect.anchoredPosition, local,
                ref _followVelocity, _followSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            return true;
        }

        /****************************************
        *                Methods
        ****************************************/

        private PlayerStatus FindNearestAlly()
        {
            PlayerStatus local = PlayerStatus.Local;
            if ((local == null) || (local.IsDead) || (!GameDB.IsInitialized))
            {
                return null;
            }

            // 관리 창이나 메뉴가 열려 있으면 G가 먹히지 않는다. 눌리지 않는 키를 띄우지 않는다
            if (!LocalGameplayInputGate.AllowsGameplayInput)
            {
                return null;
            }

            // 로비와 정산에서는 서버가 양도를 아예 받지 않는다. 눌러도 거절만 돌아온다
            RunManager run = RunManager.LocalInstance;
            if ((run == null) || (run.Phase == RunPhase.Lobby) || (run.Phase == RunPhase.Result))
            {
                return null;
            }

            // 줍기·객주 후보가 잡혀 있으면 그쪽 안내가 우선이다
            if (HasInteractableCandidate())
            {
                return null;
            }

            var spawned = NetworkClient.active ? NetworkClient.spawned : NetworkServer.spawned;
            if (spawned == null)
            {
                return null;
            }

            // 서버가 양도를 받아 줄 거리와 같은 값을 쓴다. 여기만 넓으면 눌러도 거절만 돌아온다
            float range          = GameDB.Combat.InteractRange;
            float nearestSquared = range * range;
            PlayerStatus nearest = null;

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

                if ((status == local) || (status.IsDead))
                {
                    continue;
                }

                float distanceSquared = (status.transform.position - local.transform.position).sqrMagnitude;
                if (distanceSquared > nearestSquared)
                {
                    continue;
                }

                nearestSquared = distanceSquared;
                nearest        = status;
            }

            return nearest;
        }

        private static bool HasInteractableCandidate()
        {
            NetworkIdentity owner = NetworkClient.localPlayer;
            if (owner == null)
            {
                return false;
            }

            return (owner.TryGetComponent(out InteractionController interaction)) &&
                   (interaction.CandidateNetId != 0u);
        }

        private static string ResolveInteractKey()
        {
            return ResolveInteractKeyCode().ToString();
        }

        /// <summary> 줍기·객주와 같은 키를 쓴다. 한쪽만 바뀌어 안내와 실제 키가 어긋나지 않게 한다 </summary>
        private static KeyCode ResolveInteractKeyCode()
        {
            NetworkIdentity owner = NetworkClient.localPlayer;
            if ((owner != null) && (owner.TryGetComponent(out InteractionController interaction)))
            {
                return interaction.InteractKey;
            }

            return KeyCode.G;
        }

        private string ResolveName(PlayerStatus status)
        {
            bool          hasCharacter = (status.TryGetComponent(out PlayerCharacter character)) && (character.HasCharacter);
            CharacterKind kind         = hasCharacter ? character.Character : default;

            if (status.Participant.Kind == ParticipantKind.Bot)
            {
                return (hasCharacter ? FunctionalUiText.GetCharacterName(kind) : _unknownName) + " (봇)";
            }

            if ((status.TryGetComponent(out PlayerNameTag nameTag)) && (!string.IsNullOrEmpty(nameTag.DisplayName)))
            {
                return nameTag.DisplayName;
            }

            // 이름이 아직 안 올라왔거나 KCP 개발 경로다. 직업으로라도 구분한다
            return hasCharacter ? FunctionalUiText.GetCharacterName(kind) : _unknownName;
        }

        private void Show(string text)
        {
            if ((_label != null) && (_label.text != text))
            {
                _label.text = text;
            }

            bool visible = !string.IsNullOrEmpty(text);
            if ((_root != null) && (_root.activeSelf != visible))
            {
                _root.SetActive(visible);
            }
        }
    }
}
