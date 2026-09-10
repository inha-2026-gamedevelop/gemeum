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
        *                Preload
        ****************************************/

        /// <summary>
        /// 씬에 배치하지 않아도 실행 시작에 스스로 뜬다.
        /// EnsureCreated가 기존 인스턴스와 씬 배치본을 먼저 확인하므로 중복이 생기지 않는다 -
        /// 중복이면 PersistentSingleton이 GameObject를 통째로 파괴한다
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Preload()
        {
            EnsureCreated(OBJECT_NAME);
        }

        /****************************************
        *              Constants
        ****************************************/

        private const string OBJECT_NAME = "@PartyDebugOverlay";

        private const string PANEL_ID    = "party";
        private const string PANEL_TITLE = "파티";

        // 남아 있는 개발용 키 목록. 조작은 브라우저 패널로 옮겼고, 손이 빠른 것만 키로 남겼다.
        // 키를 새로 만들면 이 배열에도 한 줄 더한다 - 안 적으면 아무도 그 키가 있는 줄 모른다
        private static readonly string[] G_KeyHelp =
        {
            "F1  호스트 시작 (KCP HUD)",
            "F2  패링 더미",
            "F3  런 포기",
            "F4  전체 부활          (호스트만)",
            "F5  보스 아레나로 전환 (호스트만)",
            "F6/F7  보스 1페이즈 / 2페이즈",
            "Ins 프레임 기록 저장",
            "Home  Steam 방 만들기   (Steam 경로)",
            "PgDn  Steam 로비 참가   (클립보드 ID)",
            "End   Steam 호스트 시작 (로비 소유자)",
            "ESC 마우스 커서",
        };

        private const int   LINE_HEIGHT     = 16;
        private const float BARS_MARGIN     = 24f; // 화면 가장자리와의 간격(px), 글자가 잘리지 않을 만큼 둔다
        private const float BAR_WIDTH       = 220f;
        private const float BAR_HEIGHT      = 9f;
        private const float BAR_GAP         = 3f;
        private const float ROW_HEIGHT      = LINE_HEIGHT + ((BAR_HEIGHT + BAR_GAP) * 3f) + 8f;
        private const float REFRESH_SECONDS = 0.2f; // 매 프레임 목록을 다시 만들면 그 자체가 부하다

        private const float NAMEPLATE_HEIGHT   = 2.2f; // 발밑에서 이만큼 위에 이름을 띄운다(m)
        private const int   NAMEPLATE_WIDTH    = 160;
        private const int   NAMEPLATE_MAX_DIST = 60;   // 이보다 멀면 글자가 겹쳐 읽을 수 없다

        /****************************************
        *                Fields
        ****************************************/

        private bool  _isBarsVisible      = true;
        private bool  _isNameplateVisible = true;
        private float _nextRefreshTime    = 0f;

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
            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.unscaledTime + REFRESH_SECONDS;
            RefreshPlayers();
        }

        protected override void OnSingletonAwake()
        {
            // 파티창은 브라우저 개발 패널로 옮겼다. 화면 왼쪽을 고정으로 덮어
            // 로비 UI와 전투 화면을 가렸고, 목록이 길면 화면 밖으로 잘렸다
            DevPanelHost.Register(PANEL_ID, PANEL_TITLE, Build, Handle);
        }

        protected override void OnSingletonDestroy()
        {
            DevPanelHost.Unregister(PANEL_ID);
        }

        private void OnGUI()
        {
            EnsureStyles();

            // 체력·스태미나·신력은 게임 화면에 있어야 한다. 3인 테스트에서 서로의 상태를 보며
            // 치유와 부활을 판단하는데, 브라우저 창을 보고 있을 수는 없다.
            // 호스트든 클라이언트든 자기 스폰 목록을 읽으므로 세 화면 모두에 뜬다
            if (_isBarsVisible)
            {
                DrawBars();
            }

            // 머리 위 이름표는 월드에 붙는 표시다
            if (_isNameplateVisible)
            {
                DrawNameplates();
            }
        }

        /****************************************
        *                Bars
        ****************************************/

        /// <summary> 화면 왼쪽 아래에 파티 상태를 쌓는다 </summary>
        private void DrawBars()
        {
            float top = Screen.height - BARS_MARGIN - (_players.Count * ROW_HEIGHT);

            for (int i = 0; i < _players.Count; ++i)
            {
                DrawPlayerBars(_players[i], BARS_MARGIN, top + (i * ROW_HEIGHT));
            }
        }

        private void DrawPlayerBars(PlayerStatus status, float left, float top)
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

            // 내 줄은 앞에 표시를 남긴다. 세 명이 같은 캐릭터를 골라도 구분된다
            if (status.isOwned)
            {
                label = "▶ " + label;
            }

            GUI.Label(new Rect(left, top, BAR_WIDTH, LINE_HEIGHT), label, _nameStyle);

            // 스태미나 · 신력 상한은 사람마다 같은 값이라 PlayerStatus가 아니라 GameDB가 정본이다
            float staminaMax = GameDB.IsInitialized ? GameDB.Combat.StaminaMax : 0f;
            float spiritMax  = GameDB.IsInitialized ? GameDB.Combat.SpiritMax  : 0f;

            float barTop = top + LINE_HEIGHT;

            DrawBar(left, barTop,                                status.Health,  status.MaxHealth, _barHealth);
            DrawBar(left, barTop + (BAR_HEIGHT + BAR_GAP),        status.Stamina, staminaMax,       _barStamina);
            DrawBar(left, barTop + ((BAR_HEIGHT + BAR_GAP) * 2f), status.Spirit,  spiritMax,        _barSpirit);
        }

        private void DrawBar(float left, float top, float value, float max, Texture2D fill)
        {
            var rect = new Rect(left, top, BAR_WIDTH, BAR_HEIGHT);

            GUI.DrawTexture(rect, _barBack);

            if (max <= 0f)
            {
                return;
            }

            float ratio = Mathf.Clamp01(value / max);

            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * ratio, rect.height), fill);
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

            // 갱신마다 순서가 뒤바뀌면 누구 줄인지 눈으로 못 따라간다
            _players.Sort(CompareByNetId);
        }

        private static int CompareByNetId(PlayerStatus left, PlayerStatus right)
        {
            return left.netId.CompareTo(right.netId);
        }

        /****************************************
        *                Panel
        ****************************************/

        private void Build(DevPanelBuilder builder)
        {
            builder.Toggle("bars",      "인게임 파티 바 (체력·스태미나·신력)", _isBarsVisible);
            builder.Toggle("nameplate", "머리 위 이름표",                       _isNameplateVisible);

            builder.Header($"파티 {_players.Count}명");

            for (int i = 0; i < _players.Count; ++i)
            {
                BuildPlayerRow(builder, _players[i]);
            }

            builder.Header("남은 단축키");

            for (int i = 0; i < G_KeyHelp.Length; ++i)
            {
                builder.Label(G_KeyHelp[i]);
            }
        }

        private void Handle(DevCommand command)
        {
            if (command.Control == "bars")
            {
                _isBarsVisible = command.IsOn;
                return;
            }

            if (command.Control == "nameplate")
            {
                _isNameplateVisible = command.IsOn;
            }
        }

        /// <summary> 막대를 그릴 수 없으니 숫자로 적는다. 3인 테스트에서 서로의 상태를 읽는 용도다 </summary>
        private static void BuildPlayerRow(DevPanelBuilder builder, PlayerStatus status)
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

            builder.Label(label);

            // 스태미나 · 신력 상한은 사람마다 같은 값이라 PlayerStatus가 아니라 GameDB가 정본이다
            float staminaMax = GameDB.IsInitialized ? GameDB.Combat.StaminaMax : 0f;
            float spiritMax  = GameDB.IsInitialized ? GameDB.Combat.SpiritMax  : 0f;

            builder.Label($"  체력 {status.Health:0}/{status.MaxHealth:0}"
                        + $"   스태미나 {status.Stamina:0}/{staminaMax:0}"
                        + $"   신력 {status.Spirit:0}/{spiritMax:0}");
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
            if (_plateStyle != null)
            {
                return;
            }

            // GUIStyle과 텍스처를 OnGUI마다 새로 만들면 프레임마다 할당이 생긴다
            _nameStyle = new GUIStyle(GUI.skin.label)
            {
                wordWrap = false,
            };

            _nameStyle.normal.textColor = Color.white;

            _plateStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,

                // 줄바꿈을 끈다. 고정 폭을 넘기면 두 줄로 접히는데 높이는 한 줄이라 아래가 잘린다
                wordWrap  = false,
            };

            _plateStyle.normal.textColor = Color.white;

            _barBack    = MakeTexture(new Color(0f, 0f, 0f, 0.6f));
            _barHealth  = MakeTexture(new Color(0.85f, 0.25f, 0.25f, 0.95f));
            _barStamina = MakeTexture(new Color(0.30f, 0.80f, 0.35f, 0.95f));
            _barSpirit  = MakeTexture(new Color(0.35f, 0.55f, 0.95f, 0.95f));
        }

        private static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            texture.SetPixel(0, 0, color);
            texture.Apply();

            return texture;
        }

#endif
    }
}
