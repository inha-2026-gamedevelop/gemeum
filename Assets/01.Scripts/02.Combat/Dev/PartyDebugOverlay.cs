// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Combat
{
    /// <summary>
    /// 왼쪽 가운데 파티창과 머리 위 이름표.
    /// UI 파트의 정식 HUD가 나오기 전까지 3인 테스트에서 서로의 상태를 읽기 위한 개발용 표시다
    /// </summary>
    public class PartyDebugOverlay : PersistentSingleton<PartyDebugOverlay>
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD

        /****************************************
        *              Constants
        ****************************************/

        private const string OBJECT_NAME = "@PartyDebugOverlay";

        // 개발용 키 목록. 코드가 아니라 사람이 읽는 안내라 여기 한곳에 모아 둔다.
        // 키를 새로 만들면 이 배열에도 한 줄 더한다 - 안 적으면 아무도 그 키가 있는 줄 모른다
        private static readonly string[] G_KeyHelp =
        {
            "F1  호스트 시작 (KCP HUD)",
            "F2  패링 더미",
            "F3  런 포기",
            "F4  전체 부활        (호스트만)",
            "F5  보스 아레나로 전환 (호스트만)",
            "F6/F7/F8  보스 1페이즈 / 2페이즈 / 토글",
            "F9  프레임 기록 저장",
            "F10 프레임 오버레이",
            "F11 카메라 조정",
            "F12 이 창",
            "ESC 마우스 커서",
        };

        private const int   PANEL_WIDTH     = 240;
        private const int   PANEL_MARGIN    = 10;
        private const int   ROW_HEIGHT      = 62; // 이름 줄 + 막대 세 개
        private const int   BAR_HEIGHT      = 9;
        private const int   BAR_GAP         = 3;
        private const int   LINE_HEIGHT     = 16;
        private const float REFRESH_SECONDS = 0.2f; // 매 프레임 목록을 다시 만들면 그 자체가 부하다

        private const float NAMEPLATE_HEIGHT   = 2.2f; // 발밑에서 이만큼 위에 이름을 띄운다(m)
        private const int   NAMEPLATE_WIDTH    = 160;
        private const int   NAMEPLATE_MAX_DIST = 60;   // 이보다 멀면 글자가 겹쳐 읽을 수 없다

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private KeyCode _toggleKey = KeyCode.F12;

        private bool  _isVisible       = true;
        private float _nextRefreshTime = 0f;

        // 매 프레임 새로 만들면 GC가 쌓인다. 갱신 주기마다 비우고 다시 채운다
        private readonly List<PlayerStatus> _players = new List<PlayerStatus>(Constants.Player.PARTY_SIZE);

        private GUIStyle  _nameStyle;
        private GUIStyle  _plateStyle;
        private Texture2D _barBack;
        private Texture2D _barHealth;
        private Texture2D _barStamina;
        private Texture2D _barSpirit;

        /****************************************
        *              Unity Event
        ****************************************/

        /// <summary> 씬이나 프리팹에 배치하지 않아도 스스로 생긴다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if ((!Application.isPlaying) || (HasInstance))
            {
                return;
            }

            EnsureCreated(OBJECT_NAME);
        }

        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey))
            {
                _isVisible = !_isVisible;
            }

            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.unscaledTime + REFRESH_SECONDS;
            RefreshPlayers();
        }

        private void OnGUI()
        {
            if (!_isVisible)
            {
                return;
            }

            EnsureStyles();
            DrawParty();
            DrawNameplates();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 스폰 목록에서 사람만 추린다. 서버든 클라이언트든 자기 사전을 본다 </summary>
        private void RefreshPlayers()
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

                if (identity.TryGetComponent(out PlayerStatus status))
                {
                    _players.Add(status);
                }
            }

            // 창마다 순서가 뒤바뀌면 누구 줄인지 눈으로 못 따라간다
            _players.Sort(CompareByNetId);
        }

        private static int CompareByNetId(PlayerStatus left, PlayerStatus right)
        {
            return left.netId.CompareTo(right.netId);
        }

        private void DrawParty()
        {
            int height = (_players.Count * ROW_HEIGHT) + LINE_HEIGHT;
            int top    = Mathf.Max(PANEL_MARGIN, (Screen.height - height) / 2);

            GUILayout.BeginArea(new Rect(PANEL_MARGIN, top, PANEL_WIDTH, height), GUI.skin.box);
            GUILayout.Label($"파티 {_players.Count}명  [{_toggleKey}]", _nameStyle);

            for (int i = 0; i < _players.Count; ++i)
            {
                DrawPlayerRow(_players[i]);
            }

            GUILayout.EndArea();

            DrawKeyHelp(top + height + PANEL_MARGIN);
        }

        /// <summary> 단축키 안내. 화면에 안 적어 두면 만든 사람만 아는 키가 된다 </summary>
        private void DrawKeyHelp(float top)
        {
            float height = (G_KeyHelp.Length + 1) * LINE_HEIGHT;
            if ((top + height) > Screen.height)
            {
                top = Mathf.Max(PANEL_MARGIN, Screen.height - height - PANEL_MARGIN);
            }

            GUILayout.BeginArea(new Rect(PANEL_MARGIN, top, PANEL_WIDTH, height), GUI.skin.box);
            GUILayout.Label("단축키", _nameStyle);

            for (int i = 0; i < G_KeyHelp.Length; ++i)
            {
                GUILayout.Label(G_KeyHelp[i], _nameStyle);
            }

            GUILayout.EndArea();
        }

        private void DrawPlayerRow(PlayerStatus status)
        {
            if (status == null)
            {
                return;
            }

            string label = ResolveName(status) + "  " + ResolveWeapon(status);
            if (status.IsDead)
            {
                label += "  (쓰러짐)";
            }

            GUILayout.Label(label, _nameStyle);

            // 스태미나 · 신력 상한은 사람마다 같은 값이라 PlayerStatus가 아니라 GameDB가 정본이다
            float staminaMax = GameDB.IsInitialized ? GameDB.Combat.StaminaMax : 0f;
            float spiritMax  = GameDB.IsInitialized ? GameDB.Combat.SpiritMax  : 0f;

            DrawBar(status.Health,  status.MaxHealth, _barHealth);
            DrawBar(status.Stamina, staminaMax,       _barStamina);
            DrawBar(status.Spirit,  spiritMax,        _barSpirit);
        }

        private void DrawBar(float value, float max, Texture2D fill)
        {
            Rect rect = GUILayoutUtility.GetRect(PANEL_WIDTH, BAR_HEIGHT + BAR_GAP);
            rect.height = BAR_HEIGHT;

            GUI.DrawTexture(rect, _barBack);

            if (max <= 0f)
            {
                return;
            }

            float ratio = Mathf.Clamp01(value / max);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * ratio, rect.height), fill);
        }

        /// <summary> 머리 위 이름표. 카메라 뒤에 있는 사람은 그리지 않는다 </summary>
        private void DrawNameplates()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            for (int i = 0; i < _players.Count; ++i)
            {
                PlayerStatus status = _players[i];
                if (status == null)
                {
                    continue;
                }

                Vector3 world  = status.transform.position + (Vector3.up * NAMEPLATE_HEIGHT);
                Vector3 screen = camera.WorldToScreenPoint(world);

                // z가 음수면 카메라 뒤다. 그대로 그리면 화면에 뒤집혀 나온다
                if ((screen.z <= 0f) || (screen.z > NAMEPLATE_MAX_DIST))
                {
                    continue;
                }

                Rect rect = new Rect(
                    screen.x - (NAMEPLATE_WIDTH * 0.5f),
                    Screen.height - screen.y - LINE_HEIGHT,
                    NAMEPLATE_WIDTH,
                    LINE_HEIGHT);

                GUI.Label(rect, ResolveName(status), _plateStyle);
            }
        }

        private static string ResolveName(PlayerStatus status)
        {
            if ((status.TryGetComponent(out PlayerNameTag tag)) && (!string.IsNullOrEmpty(tag.DisplayName)))
            {
                return tag.DisplayName;
            }

            // 이름이 아직 안 올라왔거나 KCP 경로다. 캐릭터로라도 구분한다
            return ResolveCharacter(status);
        }

        private static string ResolveCharacter(PlayerStatus status)
        {
            if ((status.TryGetComponent(out PlayerCharacter character)) && (character.HasCharacter))
            {
                return character.Character.ToString();
            }

            return "#" + status.netId;
        }

        private static string ResolveWeapon(PlayerStatus status)
        {
            if ((status.TryGetComponent(out PlayerWeapon weapon)) && (weapon.HasWeapon))
            {
                return weapon.Weapon.ToString();
            }

            return "무기 없음";
        }

        private void EnsureStyles()
        {
            if (_nameStyle != null)
            {
                return;
            }

            _nameStyle = new GUIStyle(GUI.skin.label);
            _nameStyle.normal.textColor = Color.white;

            _plateStyle = new GUIStyle(GUI.skin.label);
            _plateStyle.alignment        = TextAnchor.MiddleCenter;
            _plateStyle.normal.textColor = Color.white;

            _barBack    = MakeTexture(new Color(0f, 0f, 0f, 0.6f));
            _barHealth  = MakeTexture(new Color(0.85f, 0.25f, 0.25f, 0.95f));
            _barStamina = MakeTexture(new Color(0.30f, 0.80f, 0.35f, 0.95f));
            _barSpirit  = MakeTexture(new Color(0.35f, 0.55f, 0.95f, 0.95f));
        }

        private static Texture2D MakeTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();

            return texture;
        }

#endif
    }
}
