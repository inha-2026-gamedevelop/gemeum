// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Dev
{
    /// <summary>
    /// GDebug가 쌓은 줄을 브라우저 개발 패널로 보낸다.
    ///
    /// 태그(성공·실패·주의·서버·보스)별로 켜고 끌 수 있다 -
    /// 3인 테스트에서는 줄이 빠르게 섞여서, 보스 흐름만 또는 서버 판정만 모아 봐야 따라갈 수 있다.
    /// 줄마다 시계 시각을 붙인다. 세 화면의 로그를 맞춰 보려면 켜진 시점부터의 시간으로는 안 된다
    /// </summary>
    public class GDebugOverlay : PersistentSingleton<GDebugOverlay>
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 씬에 배치하지 않아도 첫 로그에서 스스로 생긴다.
        /// GDebug.Log는 릴리스에서 호출부만 사라지고 본문은 컴파일되므로,
        /// 이 진입점은 빌드 종류와 상관없이 존재해야 한다
        /// </summary>
        public static void EnsureExists()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS
            // 플레이 중이 아니면 만들지 않는다. DontDestroyOnLoad가 에디트 모드에서 예외를 낸다.
            // 로그는 그래도 버퍼에 쌓이므로 에디터 스크립트에서 불러도 안전하다.
            //
            // 잠겨 있을 때도 만들지 않는다. 여기는 GDebug가 로그 한 줄마다 부르는 자리라
            // 잠금 해제를 기다리는 큐에 넣으면 큐가 로그 수만큼 자란다 -
            // 대신 아래 Preload가 해제 시점에 한 번만 다시 부른다
            if ((!Application.isPlaying) || (HasInstance) || (!DevAccess.IsUnlocked))
            {
                return;
            }

            EnsureCreated(OBJECT_NAME);
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS

        /****************************************
        *              Constants
        ****************************************/

        private const string OBJECT_NAME = "@GDebugOverlay";

        private const string PANEL_ID    = "log";
        private const string PANEL_TITLE = "로그";

        private const int KIND_COUNT = 6; // GLogKind의 개수

        /****************************************
        *                Preload
        ****************************************/

        /// <summary> 로그가 한 줄도 없어도 패널은 보여야 한다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Preload()
        {
            DevAccess.RunWhenUnlocked(EnsureExists);
        }

        /****************************************
        *                Fields
        ****************************************/

        // 스냅샷마다 배열을 새로 만들지 않는다. 걸러진 줄만 앞에서부터 채우고 개수를 따로 센다
        private readonly string[] _lines = new string[GDebug.MAX_LINES];

        // 태그별로 볼지 말지
        private readonly bool[] _isKindVisible = CreateVisibleKinds();

        // 태그별 줄 수. 지금 몇 건씩 쌓였는지 알면 무엇을 켜야 할지 바로 안다
        private readonly int[] _kindCounts = new int[KIND_COUNT];

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnSingletonAwake()
        {
            DevPanelHost.Register(PANEL_ID, PANEL_TITLE, Build, Handle);
        }

        protected override void OnSingletonDestroy()
        {
            DevPanelHost.Unregister(PANEL_ID);
        }

        /****************************************
        *                Panel
        ****************************************/

        private void Build(DevPanelBuilder builder)
        {
            int count = GDebug.Count;
            int shown = 0;

            for (int i = 0; i < KIND_COUNT; ++i)
            {
                _kindCounts[i] = 0;
            }

            // 오래된 것이 위, 새 것이 아래다. 켜 둔 태그만 담는다
            for (int i = 0; i < count; ++i)
            {
                GDebug.Entry entry = GDebug.GetEntry(i);

                int kindIndex = (int) entry.Kind;

                if ((kindIndex >= 0) && (kindIndex < KIND_COUNT))
                {
                    ++_kindCounts[kindIndex];

                    if (!_isKindVisible[kindIndex])
                    {
                        continue;
                    }
                }

                _lines[shown] = BuildLine(entry);
                ++shown;
            }

            builder.Label($"{shown}줄 표시 / 전체 {count}줄");

            builder.Header("태그");

            for (int i = 0; i < KIND_COUNT; ++i)
            {
                var kind = (GLogKind) i;

                builder.Toggle("kind." + i, $"{ResolveTagName(kind)} ({_kindCounts[i]})",
                    _isKindVisible[i]);
            }

            builder.Button("only.server", "서버만 보기");
            builder.Button("only.boss",   "보스만 보기");
            builder.Button("all",         "전부 보기");

            builder.Header("줄");
            builder.Toggle("console", "Unity 콘솔에도 남긴다", GDebug.MirrorToUnityConsole);
            builder.Button("clear", "비우기");

            builder.Log("lines", _lines, shown);
        }

        private void Handle(DevCommand command)
        {
            switch (command.Control)
            {
                case "clear":
                    GDebug.Clear();
                    return;

                case "console":
                    GDebug.MirrorToUnityConsole = command.IsOn;
                    return;

                case "all":
                    SetAllVisible(true);
                    return;

                case "only.server":
                    ShowOnly(GLogKind.Server);
                    return;

                case "only.boss":
                    ShowOnly(GLogKind.Boss);
                    return;
            }

            if ((string.IsNullOrEmpty(command.Control)) || (!command.Control.StartsWith("kind.")))
            {
                return;
            }

            if (!int.TryParse(command.Control.Substring("kind.".Length), out int index))
            {
                return;
            }

            if ((index >= 0) && (index < KIND_COUNT))
            {
                _isKindVisible[index] = command.IsOn;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 한 줄에 시각과 태그를 붙인다. 앞의 성격 이름은 브라우저가 색을 나누는 데 쓴다 </summary>
        private static string BuildLine(in GDebug.Entry entry)
        {
            string body = entry.Message;

            if (entry.Repeat > 1)
            {
                body += "  x" + entry.Repeat;
            }

            return ResolveKindName(entry.Kind) + "|"
                 + entry.WallClock.ToString("HH:mm:ss") + "  "
                 + ResolveTagPrefix(entry.Kind) + body;
        }

        private void SetAllVisible(bool isVisible)
        {
            for (int i = 0; i < KIND_COUNT; ++i)
            {
                _isKindVisible[i] = isVisible;
            }
        }

        private void ShowOnly(GLogKind kind)
        {
            SetAllVisible(false);
            _isKindVisible[(int) kind] = true;
        }

        private static bool[] CreateVisibleKinds()
        {
            var visible = new bool[KIND_COUNT];

            for (int i = 0; i < KIND_COUNT; ++i)
            {
                visible[i] = true;
            }

            return visible;
        }

        /// <summary> 패널 토글에 뜨는 태그 이름 </summary>
        private static string ResolveTagName(GLogKind kind)
        {
            switch (kind)
            {
                case GLogKind.Success: return "성공";
                case GLogKind.Fail:    return "실패";
                case GLogKind.Warn:    return "주의";
                case GLogKind.Server:  return "서버";
                case GLogKind.Boss:    return "보스";
                default:               return "정보";
            }
        }

        /// <summary> 줄 앞에 붙는 태그. 섞여 있을 때 눈으로 갈라 보게 한다 </summary>
        private static string ResolveTagPrefix(GLogKind kind)
        {
            return (kind == GLogKind.Info) ? string.Empty : "[" + ResolveTagName(kind) + "] ";
        }

        /// <summary> 브라우저의 CSS 클래스 이름이 된다 </summary>
        private static string ResolveKindName(GLogKind kind)
        {
            switch (kind)
            {
                case GLogKind.Success: return "success";
                case GLogKind.Fail:    return "fail";
                case GLogKind.Warn:    return "warn";
                case GLogKind.Server:  return "server";
                case GLogKind.Boss:    return "boss";
                default:               return "info";
            }
        }

#endif
    }
}
