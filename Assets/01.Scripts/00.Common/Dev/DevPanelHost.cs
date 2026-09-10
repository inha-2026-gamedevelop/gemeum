// System
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Geumeum.Common
{
    /// <summary>
    /// 개발용 패널을 모아 브라우저에 보낼 상태를 만들고, 브라우저가 누른 것을 게임에 적용한다.
    ///
    /// 게임 화면 위에 IMGUI로 그리면 전투 화면을 덮고 창 크기가 게임 창에 갇힌다.
    /// 그래서 조작은 별도 브라우저 창에 두고, 게임 화면에는 월드에 붙는 표시만 남긴다.
    /// 통신은 DevWebServer가 하고, 여기는 무엇을 보여 줄지와 무엇을 눌렀는지만 다룬다
    /// </summary>
    public class DevPanelHost : PersistentSingleton<DevPanelHost>
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 패널 하나를 등록한다. 호스트가 아직 없어도 안전하다.
        /// build는 주기마다 현재 값을 다시 적고, handle은 브라우저가 누른 것을 받는다.
        /// 둘 다 메인 스레드에서만 불린다
        /// </summary>
        public static void Register(string id, string title, Action<DevPanelBuilder> build,
                                    Action<DevCommand> handle)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if ((string.IsNullOrEmpty(id)) || (build == null))
            {
                return;
            }

            for (int i = 0; i < _panels.Count; ++i)
            {
                if (_panels[i].Id == id)
                {
                    return;
                }
            }

            _panels.Add(new Panel(id, title, build, handle));

            if (Application.isPlaying)
            {
                EnsureCreated(OBJECT_NAME);
            }
#endif
        }

        /// <summary> 등록을 지운다. 패널을 만든 쪽이 파괴될 때 부른다 </summary>
        public static void Unregister(string id)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            for (int i = 0; i < _panels.Count; ++i)
            {
                if (_panels[i].Id == id)
                {
                    _panels.RemoveAt(i);
                    return;
                }
            }
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD

        /****************************************
        *              Constants
        ****************************************/

        private const string OBJECT_NAME = "@DevPanelHost";

        // 브라우저는 이 주기로 상태를 받아 간다. 더 잘게 만들면 게임 쪽 비용만 늘고 눈에 차이가 없다
        private const float SNAPSHOT_INTERVAL = 0.1f;

        private const int MAX_QUEUED_COMMANDS = 64;

        /****************************************
        *                Types
        ****************************************/

        private sealed class Panel
        {
            public readonly string                  Id;
            public readonly string                  Title;
            public readonly Action<DevPanelBuilder> Build;
            public readonly Action<DevCommand>      Handle;

            public Panel(string id, string title, Action<DevPanelBuilder> build, Action<DevCommand> handle)
            {
                Id     = id;
                Title  = title;
                Build  = build;
                Handle = handle;
            }
        }

        /// <summary> 브라우저가 보내는 조작. JsonUtility가 읽을 수 있게 필드만 둔다 </summary>
        [Serializable]
        private class CommandBody
        {
            public string panel;
            public string control;
            public string value;
        }

        /****************************************
        *                Fields
        ****************************************/

        // 호스트가 생기기 전에 등록될 수 있으므로 static이다
        private static readonly List<Panel> _panels = new List<Panel>(16);

        // 서버 스레드가 넣고 메인 스레드가 꺼낸다. Unity API는 메인 스레드에서만 부를 수 있다
        private static readonly ConcurrentQueue<string> _commands = new ConcurrentQueue<string>();

        // 서버 스레드가 그대로 내보내는 문자열. 참조 교체만 하므로 잠글 필요가 없다
        private static volatile string _snapshotJson = "{}";

        private readonly DevPanelBuilder _builder = new DevPanelBuilder();

        private float _nextSnapshotTime = 0f;
        private int   _revision         = 0;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지금 상태. 서버 스레드가 읽는다 </summary>
        public static string CurrentSnapshotJson => _snapshotJson;

        /****************************************
        *                Preload
        ****************************************/

        /// <summary> 씬에 배치하지 않아도 스스로 뜬다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Preload()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            EnsureCreated(OBJECT_NAME);
        }

        /// <summary> 도메인 리로드를 끈 환경에서 이전 플레이의 등록이 남지 않게 한다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry()
        {
            _panels.Clear();

            while (_commands.TryDequeue(out string _))
            {
            }

            _snapshotJson = "{}";
            ResetStatic();
        }

        /****************************************
        *              Server Bridge
        ****************************************/

        /// <summary>
        /// 브라우저가 보낸 조작을 줄 세운다. 서버 스레드에서 불린다.
        /// 여기서 바로 적용하면 Unity API를 다른 스레드에서 부르게 되어 그 자리에서 예외가 난다
        /// </summary>
        public static void EnqueueCommandBody(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return;
            }

            // 브라우저가 폭주해도 큐가 무한히 자라지 않게 한다
            if (_commands.Count >= MAX_QUEUED_COMMANDS)
            {
                return;
            }

            _commands.Enqueue(body);
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            ApplyQueuedCommands();

            if (Time.unscaledTime < _nextSnapshotTime)
            {
                return;
            }

            _nextSnapshotTime = Time.unscaledTime + SNAPSHOT_INTERVAL;
            RebuildSnapshot();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void ApplyQueuedCommands()
        {
            while (_commands.TryDequeue(out string body))
            {
                CommandBody parsed = null;

                try
                {
                    parsed = JsonUtility.FromJson<CommandBody>(body);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[DevPanelHost] 조작을 읽지 못했다: {exception.Message}", this);
                    continue;
                }

                if ((parsed == null) || (string.IsNullOrEmpty(parsed.panel)))
                {
                    continue;
                }

                Dispatch(new DevCommand(parsed.panel, parsed.control, parsed.value));
            }
        }

        private void Dispatch(in DevCommand command)
        {
            for (int i = 0; i < _panels.Count; ++i)
            {
                Panel panel = _panels[i];
                if (panel.Id != command.Panel)
                {
                    continue;
                }

                // 개발 도구가 게임을 멈추면 안 된다. 한 패널이 터져도 나머지는 계속 받는다
                try
                {
                    panel.Handle?.Invoke(command);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }

                return;
            }
        }

        private void RebuildSnapshot()
        {
            var views = new DevPanelView[_panels.Count];

            for (int i = 0; i < _panels.Count; ++i)
            {
                Panel panel = _panels[i];

                _builder.Clear();

                try
                {
                    panel.Build(_builder);
                }
                catch (Exception exception)
                {
                    _builder.Clear();
                    _builder.Label("이 패널을 만들다 예외가 났다: " + exception.Message);
                    Debug.LogException(exception, this);
                }

                views[i] = new DevPanelView
                {
                    id       = panel.Id,
                    title    = panel.Title,
                    controls = _builder.ToArray(),
                };
            }

            ++_revision;

            var snapshot = new DevSnapshotView
            {
                product  = Application.productName,
                role     = DescribeRole(),
                revision = _revision,
                panels   = views,
            };

            _snapshotJson = JsonUtility.ToJson(snapshot);
        }

        /// <summary> 3대를 나란히 열어 두면 어느 창이 어느 프로세스인지 헷갈린다 </summary>
        private static string DescribeRole()
        {
            if ((NetworkServer.active) && (NetworkClient.active))
            {
                return "호스트";
            }

            if (NetworkServer.active)
            {
                return "서버";
            }

            if (NetworkClient.active)
            {
                return "클라이언트";
            }

            return "오프라인";
        }

#endif
    }
}
