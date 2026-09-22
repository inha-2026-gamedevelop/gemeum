// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Combat;
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Dev;

namespace Geumeum.UI
{
    /// <summary>
    /// 서버가 모든 플레이어의 체력, 스태미나, 신력을 조금씩 깎는다. HUD 바가 실제로 값을 따라 움직이는지
    /// 보려는 용도다
    ///
    /// Dev_HUD에는 캐릭터 조작이 없어 자원을 쓸 방법이 없다. 값을 흔들어 주는 쪽을 따로 두지 않으면
    /// 바가 가득 찬 그림만 보고 "붙었다"고 넘어가게 된다
    ///
    /// 판정은 서버에서만 한다 - 클라이언트가 값을 직접 만지면 사람마다 다른 화면이 나온다
    /// 기존 서버 API(TakeDamage, TrySpendStamina, TrySpendSpirit)만 부르므로 자원 규칙을 우회하지 않는다
    ///
    /// 릴리스 빌드에는 들어가지 않는다
    /// </summary>
    public class HudResourceDecayTester : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS

        /****************************************
        *                Preload
        ****************************************/

        /// <summary> 씬에 배치하지 않아도 실행 시작에 스스로 붙는다. 기본은 꺼짐이다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Preload()
        {
            DevToolHost.Attach<HudResourceDecayTester>();
        }

        /****************************************
        *              Constants
        ****************************************/

        private const string PANEL_ID    = "huddecay";
        private const string PANEL_TITLE = "HUD 감쇠";

        private const string CONTROL_RUN      = "run";
        private const string CONTROL_INTERVAL = "interval";
        private const string CONTROL_AMOUNT   = "amount";
        private const string CONTROL_FLOOR    = "floor";

        private const float DEFAULT_INTERVAL = 0.25f;
        private const float MIN_INTERVAL     = 0.05f;
        private const float MAX_INTERVAL     = 2f;

        // 최대치 대비 비율로 깎는다. 사람마다 최대 체력이 달라도 바가 같은 속도로 줄어 보인다
        private const float DEFAULT_AMOUNT = 0.01f;
        private const float MIN_AMOUNT     = 0.001f;
        private const float MAX_AMOUNT     = 0.1f;

        // 기준 양의 이 사이에서 고른다. 세 자원이 같은 박자로 줄면 무엇이 무엇인지 구분이 안 된다
        private const float JITTER_LOW  = 0.2f;
        private const float JITTER_HIGH = 1f;

        // 이 비율 밑으로는 깎지 않는다. 0으로 내리면 쓰러짐 표시까지 볼 수 있다
        private const float DEFAULT_FLOOR = 0.15f;
        private const float MAX_FLOOR     = 0.9f;

        /****************************************
        *                Fields
        ****************************************/

        private bool  _isRunning    = false;
        private float _interval     = DEFAULT_INTERVAL;
        private float _amount       = DEFAULT_AMOUNT;
        private float _floor        = DEFAULT_FLOOR;
        private float _nextTickTime = 0f;

        // 매 tick 새로 만들면 GC가 쌓인다. 주기마다 비우고 다시 채운다
        private readonly List<PlayerStatus> _players = new List<PlayerStatus>(Constants.Player.PARTY_SIZE);

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            DevPanelHost.Register(PANEL_ID, PANEL_TITLE, Build, Handle);
        }

        private void OnDestroy()
        {
            DevPanelHost.Unregister(PANEL_ID);
        }

        private void Update()
        {
            if ((!_isRunning) || (!NetworkServer.active))
            {
                return;
            }

            if (Time.unscaledTime < _nextTickTime)
            {
                return;
            }

            _nextTickTime = Time.unscaledTime + _interval;
            Tick();
        }

        /****************************************
        *                 Decay
        ****************************************/

        private void Tick()
        {
            CollectPlayers();

            for (int i = 0; i < _players.Count; ++i)
            {
                DecayOne(_players[i]);
            }
        }

        private void CollectPlayers()
        {
            _players.Clear();

            if (NetworkServer.spawned == null)
            {
                return;
            }

            foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
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
        }

        private void DecayOne(PlayerStatus status)
        {
            if ((status == null) || status.IsDead || status.IsSwallowed)
            {
                return;
            }

            DrainHealth(status);

            if (!GameDB.IsInitialized)
            {
                return; // 스태미나, 신력 상한은 GameDB가 정본이다
            }

            DrainStamina(status);
            DrainSpirit(status);
        }

        private void DrainHealth(PlayerStatus status)
        {
            float max = status.MaxHealth;
            if (max <= 0f)
            {
                return;
            }

            float amount = NextAmount(max);
            if ((status.Health - amount) <= (max * _floor))
            {
                return;
            }

            status.TakeDamage(DamageInfo.FromEnvironment(amount));
        }

        private void DrainStamina(PlayerStatus status)
        {
            float max = GameDB.Combat.StaminaMax;
            if (max <= 0f)
            {
                return;
            }

            float amount = NextAmount(max);
            if ((status.Stamina - amount) <= (max * _floor))
            {
                return;
            }

            status.TrySpendStamina(amount);
        }

        private void DrainSpirit(PlayerStatus status)
        {
            float max = GameDB.Combat.SpiritMax;
            if (max <= 0f)
            {
                return;
            }

            float amount = NextAmount(max);
            if ((status.Spirit - amount) <= (max * _floor))
            {
                return;
            }

            status.TrySpendSpirit(amount);
        }

        private float NextAmount(float max)
        {
            return max * _amount * Random.Range(JITTER_LOW, JITTER_HIGH);
        }

        /****************************************
        *                 Panel
        ****************************************/

        private void Build(DevPanelBuilder builder)
        {
            builder.Header("HUD 자원 감쇠");

            if (!NetworkServer.active)
            {
                builder.Label("호스트에서만 동작한다. 런을 먼저 열 것");
                return;
            }

            builder.Toggle(CONTROL_RUN, "감쇠 켜기", _isRunning);
            builder.Label("모든 플레이어의 체력 · 스태미나 · 신력을 주기마다 무작위로 조금씩 깎는다");

            builder.Slider(CONTROL_INTERVAL, "주기(초)", _interval, MIN_INTERVAL, MAX_INTERVAL);
            builder.Slider(CONTROL_AMOUNT, "한 번에 깎을 비율", _amount, MIN_AMOUNT, MAX_AMOUNT);
            builder.Slider(CONTROL_FLOOR, "남겨 둘 비율", _floor, 0f, MAX_FLOOR);
            builder.Label("남겨 둘 비율을 0으로 내리면 체력이 바닥나 쓰러짐 표시까지 확인할 수 있다");

            // 되돌리는 것은 치트 패널의 무한 자원이 이미 한다. 같은 일을 두 군데 두지 않는다
            builder.Label("값을 되돌리려면 치트 패널의 무한 자원을 켰다 끈다");

            CollectPlayers();
            builder.Label($"대상 {_players.Count}명");

            for (int i = 0; i < _players.Count; ++i)
            {
                PlayerStatus status = _players[i];
                if (status == null)
                {
                    continue;
                }

                builder.Label($"  체력 {status.Health:F0} / {status.MaxHealth:F0}"
                            + $"   스태미나 {status.Stamina:F0}   신력 {status.Spirit:F0}");
            }
        }

        private void Handle(DevCommand command)
        {
            switch (command.Control)
            {
                case CONTROL_RUN:
                    _isRunning    = command.IsOn;
                    _nextTickTime = 0f;
                    break;

                case CONTROL_INTERVAL:
                    if (command.TryGetFloat(out float interval))
                    {
                        _interval = Mathf.Clamp(interval, MIN_INTERVAL, MAX_INTERVAL);
                    }

                    break;

                case CONTROL_AMOUNT:
                    if (command.TryGetFloat(out float amount))
                    {
                        _amount = Mathf.Clamp(amount, MIN_AMOUNT, MAX_AMOUNT);
                    }

                    break;

                case CONTROL_FLOOR:
                    if (command.TryGetFloat(out float floor))
                    {
                        _floor = Mathf.Clamp(floor, 0f, MAX_FLOOR);
                    }

                    break;

                default:
                    break;
            }
        }

#endif
    }
}
