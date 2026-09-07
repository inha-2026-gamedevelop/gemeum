// System
using System.Diagnostics;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 화면 로그 한 줄의 성격. 색과 접두어를 정한다 </summary>
    public enum GLogKind
    {
        Info    = 0,
        Success = 1,
        Fail    = 2,
        Warn    = 3,
        Server  = 4, // 서버 판정 결과, 3인 테스트에서 누가 정했는지 구분하려고 나눈다
    }

    /// <summary> 화면 오른쪽에 뜨는 개발용 로그. 게임을 멈추지 않고 판정 결과를 본다 </summary>
    public static class GDebug
    {
        /****************************************
        *              Constants
        ****************************************/

        public const int MAX_LINES = 18; // 오른쪽 화면에 겹치지 않고 들어가는 줄 수

        private const int MAX_MESSAGE_LENGTH = 120; // 넘으면 잘라서 한 줄을 유지한다

        /****************************************
        *                Types
        ****************************************/

        /// <summary> 화면에 떠 있는 줄 하나 </summary>
        public struct Entry
        {
            public GLogKind Kind;
            public string   Message;
            public float    UnscaledTime; // 히트스톱이나 슬로우모션에 로그가 얼어붙지 않게 한다
            public int      Repeat;       // 같은 줄이 연달아 오면 늘린다
        }

        /****************************************
        *                Fields
        ****************************************/

        // 링 버퍼. 매 줄마다 리스트를 늘리면 판정 루프에서 GC가 돈다
        private static readonly Entry[] _entries = new Entry[MAX_LINES];

        private static int _count = 0; // 채워진 줄 수
        private static int _head  = 0; // 다음에 쓸 자리

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지금 떠 있는 줄 수 </summary>
        public static int Count => _count;

        /// <summary> 화면 표시를 잠시 끈다. 로그 자체는 계속 쌓인다 </summary>
        public static bool IsVisible { get; set; } = true;

        /// <summary> 같은 내용을 Unity 콘솔에도 남긴다. 기본은 꺼 둔다, 콘솔이 시끄러워지면 진짜 에러를 놓친다 </summary>
        public static bool MirrorToUnityConsole { get; set; } = false;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 화면에 한 줄 띄운다 </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Log(string message)
        {
            Write(GLogKind.Info, message);
        }

        /// <summary> 성격을 정해 한 줄 띄운다 </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Log(GLogKind kind, string message)
        {
            Write(kind, message);
        }

        /// <summary> 성공한 판정 </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Success(string message)
        {
            Write(GLogKind.Success, message);
        }

        /// <summary> 실패하거나 거부된 판정 </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Fail(string message)
        {
            Write(GLogKind.Fail, message);
        }

        /// <summary> 이상하지만 진행은 되는 것 </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Warn(string message)
        {
            Write(GLogKind.Warn, message);
        }

        /// <summary> 서버가 확정한 사실. 3인 테스트에서 누가 정한 값인지 구분한다 </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Server(string message)
        {
            Write(GLogKind.Server, message);
        }

        /// <summary> 화면을 비운다 </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Clear()
        {
            _count = 0;
            _head  = 0;
        }

        /// <summary> 오래된 것부터 순서대로 읽는다. 화면에 그리는 쪽이 쓴다 </summary>
        public static Entry GetEntry(int index)
        {
            // 범위를 벗어나면 빈 줄을 돌려준다. 디버그 도구가 게임을 죽이면 안 된다
            if ((index < 0) || (index >= _count))
            {
                return default;
            }

            int start = (_count < MAX_LINES) ? 0 : _head;
            return _entries[(start + index) % MAX_LINES];
        }

        /// <summary> 도메인 리로드를 끈 환경에서 이전 플레이의 줄이 남지 않게 한다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            _count = 0;
            _head  = 0;
        }

        private static void Write(GLogKind kind, string message)
        {
            if (message == null)
            {
                message = "(null)";
            }

            if (message.Length > MAX_MESSAGE_LENGTH)
            {
                message = message.Substring(0, MAX_MESSAGE_LENGTH);
            }

            float now = Time.unscaledTime;

            // 같은 줄이 연달아 오면 새로 쌓지 않고 횟수만 올린다.
            // 매 프레임 도는 자리에 로그를 걸어도 화면이 한 줄로 덮이지 않는다
            if (_count > 0)
            {
                int lastIndex = (_head - 1 + MAX_LINES) % MAX_LINES;

                if ((_entries[lastIndex].Kind == kind) && (_entries[lastIndex].Message == message))
                {
                    _entries[lastIndex].Repeat      += 1;
                    _entries[lastIndex].UnscaledTime = now;
                    MirrorIfNeeded(kind, message);
                    return;
                }
            }

            _entries[_head] = new Entry
            {
                Kind         = kind,
                Message      = message,
                UnscaledTime = now,
                Repeat       = 1,
            };

            _head = (_head + 1) % MAX_LINES;

            if (_count < MAX_LINES)
            {
                ++_count;
            }

            MirrorIfNeeded(kind, message);

            GDebugOverlay.EnsureExists();
        }

        private static void MirrorIfNeeded(GLogKind kind, string message)
        {
            if (!MirrorToUnityConsole)
            {
                return;
            }

            if (kind == GLogKind.Fail)
            {
                UnityEngine.Debug.LogError(message);
                return;
            }

            if (kind == GLogKind.Warn)
            {
                UnityEngine.Debug.LogWarning(message);
                return;
            }

            UnityEngine.Debug.Log(message);
        }
    }
}
