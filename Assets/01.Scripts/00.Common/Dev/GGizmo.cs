// System
using System.Collections.Generic;
using System.Diagnostics;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 기즈모를 묶는 갈래. 창에서 갈래별로 껐다 켠다 </summary>
    public enum GGizmoChannel
    {
        Camera  = 0,
        Combat  = 1,
        Barrier = 2,
        Monster = 3,
        Network = 4,
        Misc    = 5,
    }

    /// <summary>
    /// 게임 화면에 선과 글자를 그려 값을 눈으로 확인하는 개발용 도구.
    /// 한 프레임에 여러 후보 값을 한번에 비교해야 하는 경우 브레이크포인트로는 못 보므로 그림으로 본다.
    /// 그리는 일은 GGizmoRenderer가 하고, 여기는 무엇을 그릴지 모아 두는 곳이다
    /// </summary>
    public static class GGizmo
    {
        /****************************************
        *              Constants
        ****************************************/

        public const int CHANNEL_COUNT = 6;

        /// <summary> 원·구·부채꼴을 몇 조각의 선으로 쪼갤지 </summary>
        private const int CIRCLE_STEPS = 24;

        private const int MAX_SEGMENTS = 6000; // 한 프레임에 담을 선의 최대 개수
        private const int MAX_LABELS   = 256;
        private const int MAX_WATCHES  = 64;
        private const int MAX_PLOTS    = 12;

        /// <summary> 값 표시는 이만큼 갱신이 없으면 사라진다. 지난 상황의 값이 남아 오해를 만든다 </summary>
        private const float WATCH_LIFETIME = 3f;

        public const int PLOT_SAMPLES = 240;

        /****************************************
        *                Types
        ****************************************/

        /// <summary> 선 한 조각. 모든 도형은 결국 선으로 쪼개져 여기에 쌓인다 </summary>
        public struct Segment
        {
            public Vector3       A;
            public Vector3       B;
            public Color         Color;
            public GGizmoChannel Channel;
            public float         ExpireTime; // 0이면 이 프레임만 산다
            public int           Frame;
        }

        /// <summary> 월드 좌표에 붙는 글자 </summary>
        public struct Label
        {
            public Vector3       World;
            public string        Text;
            public Color         Color;
            public GGizmoChannel Channel;
            public float         ExpireTime;
            public int           Frame;
        }

        /// <summary> 지금 값을 이름과 함께 보여 주는 한 줄 </summary>
        public struct Watch
        {
            public string        Key;
            public string        Value;
            public GGizmoChannel Channel;
            public float         Time;
        }

        /// <summary> 여러 프레임에 걸쳐 변하는 값. 창에서 꺾은선으로 본다 </summary>
        public sealed class Plot
        {
            public string  Key;
            public float[] Samples = new float[PLOT_SAMPLES];
            public int     Count;
            public int     Head;
            public float   Time;

            /// <summary> 오래된 것부터 순서대로 읽는다 </summary>
            public float Get(int index)
            {
                int start = (Count < PLOT_SAMPLES) ? 0 : Head;
                return Samples[(start + index) % PLOT_SAMPLES];
            }

            public void Add(float value)
            {
                Samples[Head] = value;
                Head          = (Head + 1) % PLOT_SAMPLES;

                if (Count < PLOT_SAMPLES)
                {
                    ++Count;
                }

                Time = UnityEngine.Time.unscaledTime;
            }
        }

        /****************************************
        *                Fields
        ****************************************/

        // 배열로 잡아 둔다. 매 프레임 리스트를 새로 만들면 판정 루프에서 GC가 돈다
        private static readonly Segment[] _segments = new Segment[MAX_SEGMENTS];
        private static int _segmentCount = 0;

        private static readonly Label[] _labels = new Label[MAX_LABELS];
        private static int _labelCount = 0;

        private static readonly List<Watch> _watches = new List<Watch>(MAX_WATCHES);
        private static readonly List<Plot>  _plots   = new List<Plot>(MAX_PLOTS);

        private static readonly bool[] _isChannelVisible = CreateVisibleChannels();

        private static bool _isOverflowed = false;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 전체를 껐다 켠다 </summary>
        public static bool IsEnabled { get; set; } = true;

        /// <summary> 지형에 가려진 부분도 그릴지. 기본은 가리지 않고 다 그린다 </summary>
        public static bool UseDepthTest { get; set; } = false;

        /// <summary> 월드에 붙는 글자를 그릴지 </summary>
        public static bool ShowLabels { get; set; } = true;

        /// <summary> 지금 쌓여 있는 선의 개수. 창에서 예산을 확인한다 </summary>
        public static int SegmentCount => _segmentCount;

        public static int LabelCount => _labelCount;

        /// <summary> 한 프레임 예산을 넘겨 버린 것이 있는지 </summary>
        public static bool IsOverflowed => _isOverflowed;

        public static IReadOnlyList<Watch> Watches => _watches;

        public static IReadOnlyList<Plot> Plots => _plots;

        /****************************************
        *              Visibility
        ****************************************/

        public static bool IsChannelVisible(GGizmoChannel channel)
        {
            int index = (int) channel;
            return ((index >= 0) && (index < CHANNEL_COUNT) && (_isChannelVisible[index]));
        }

        public static void SetChannelVisible(GGizmoChannel channel, bool isVisible)
        {
            int index = (int) channel;
            if ((index < 0) || (index >= CHANNEL_COUNT))
            {
                return;
            }

            _isChannelVisible[index] = isVisible;
        }

        /// <summary> 창에 뜨는 갈래 이름 </summary>
        public static string GetChannelName(GGizmoChannel channel)
        {
            switch (channel)
            {
                case GGizmoChannel.Camera:  return "카메라";
                case GGizmoChannel.Combat:  return "전투";
                case GGizmoChannel.Barrier: return "결계";
                case GGizmoChannel.Monster: return "몬스터";
                case GGizmoChannel.Network: return "네트워크";
                default:                    return "기타";
            }
        }

        /****************************************
        *                Shapes
        ****************************************/

        /// <summary> 선 하나. duration이 0이면 이 프레임만 보인다 </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Line(GGizmoChannel channel, Vector3 from, Vector3 to, Color color, float duration = 0f)
        {
            AddSegment(channel, from, to, color, duration);
        }

        /// <summary> 방향과 길이를 가진 화살표. 카메라가 보는 방향이나 밀어내는 힘을 볼 때 쓴다 </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Arrow(GGizmoChannel channel, Vector3 origin, Vector3 direction, Color color,
                                 float duration = 0f)
        {
            Vector3 tip = origin + direction;
            AddSegment(channel, origin, tip, color, duration);

            float length = direction.magnitude;
            if (length <= Mathf.Epsilon)
            {
                return;
            }

            // 촉은 진행 방향에 수직인 두 선으로 만든다. 어디가 끝인지 한눈에 보이게 한다
            Vector3 forward = direction / length;
            Vector3 side    = Vector3.Cross(forward, Vector3.up);

            if (side.sqrMagnitude <= 0.0001f)
            {
                side = Vector3.Cross(forward, Vector3.forward);
            }

            side = side.normalized * (length * 0.12f);

            Vector3 back = tip - (forward * (length * 0.2f));

            AddSegment(channel, tip, back + side, color, duration);
            AddSegment(channel, tip, back - side, color, duration);
        }

        /// <summary> 위치 하나를 십자로 찍는다 </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Point(GGizmoChannel channel, Vector3 position, float size, Color color,
                                 float duration = 0f)
        {
            float half = size * 0.5f;

            AddSegment(channel, position - (Vector3.right   * half), position + (Vector3.right   * half), color, duration);
            AddSegment(channel, position - (Vector3.up      * half), position + (Vector3.up      * half), color, duration);
            AddSegment(channel, position - (Vector3.forward * half), position + (Vector3.forward * half), color, duration);
        }

        /// <summary> 법선이 normal인 평면 위의 원 </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Circle(GGizmoChannel channel, Vector3 center, Vector3 normal, float radius, Color color,
                                  float duration = 0f)
        {
            GetPlane(normal, out Vector3 right, out Vector3 forward);

            Vector3 previous = center + (right * radius);

            for (int i = 1; i <= CIRCLE_STEPS; ++i)
            {
                float radian = (Mathf.PI * 2f) * ((float) i / CIRCLE_STEPS);

                Vector3 current = center
                                + (right   * (Mathf.Cos(radian) * radius))
                                + (forward * (Mathf.Sin(radian) * radius));

                AddSegment(channel, previous, current, color, duration);
                previous = current;
            }
        }

        /// <summary> 세 방향의 원으로 그린 구. 사거리와 감지 범위를 볼 때 쓴다 </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Sphere(GGizmoChannel channel, Vector3 center, float radius, Color color,
                                  float duration = 0f)
        {
            Circle(channel, center, Vector3.up,      radius, color, duration);
            Circle(channel, center, Vector3.right,   radius, color, duration);
            Circle(channel, center, Vector3.forward, radius, color, duration);
        }

        /// <summary> 회전을 가진 상자. 판정 박스를 볼 때 쓴다 </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Box(GGizmoChannel channel, Vector3 center, Vector3 size, Quaternion rotation, Color color,
                               float duration = 0f)
        {
            Vector3 half = size * 0.5f;

            // 아래 네 점과 위 네 점
            Vector3 a = center + (rotation * new Vector3(-half.x, -half.y, -half.z));
            Vector3 b = center + (rotation * new Vector3( half.x, -half.y, -half.z));
            Vector3 c = center + (rotation * new Vector3( half.x, -half.y,  half.z));
            Vector3 d = center + (rotation * new Vector3(-half.x, -half.y,  half.z));

            Vector3 e = center + (rotation * new Vector3(-half.x,  half.y, -half.z));
            Vector3 f = center + (rotation * new Vector3( half.x,  half.y, -half.z));
            Vector3 g = center + (rotation * new Vector3( half.x,  half.y,  half.z));
            Vector3 h = center + (rotation * new Vector3(-half.x,  half.y,  half.z));

            AddSegment(channel, a, b, color, duration);
            AddSegment(channel, b, c, color, duration);
            AddSegment(channel, c, d, color, duration);
            AddSegment(channel, d, a, color, duration);

            AddSegment(channel, e, f, color, duration);
            AddSegment(channel, f, g, color, duration);
            AddSegment(channel, g, h, color, duration);
            AddSegment(channel, h, e, color, duration);

            AddSegment(channel, a, e, color, duration);
            AddSegment(channel, b, f, color, duration);
            AddSegment(channel, c, g, color, duration);
            AddSegment(channel, d, h, color, duration);
        }

        /// <summary>
        /// 바닥에 눕는 부채꼴. 각도 판정을 눈으로 보는 용도다 -
        /// 결계 방향 판정이나 전방 공격 판정처럼 "몇 도까지 맞는가"를 숫자로만 보면 틀린 것을 못 찾는다
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Arc(GGizmoChannel channel, Vector3 center, Vector3 forward, float halfAngle, float radius,
                               Color color, float duration = 0f)
        {
            Vector3 flat = new Vector3(forward.x, 0f, forward.z);
            if (flat.sqrMagnitude <= 0.0001f)
            {
                flat = Vector3.forward;
            }

            flat.Normalize();

            Vector3 first = center + (Quaternion.AngleAxis(-halfAngle, Vector3.up) * flat * radius);
            Vector3 last  = center + (Quaternion.AngleAxis( halfAngle, Vector3.up) * flat * radius);

            AddSegment(channel, center, first, color, duration);
            AddSegment(channel, center, last,  color, duration);

            Vector3 previous = first;

            for (int i = 1; i <= CIRCLE_STEPS; ++i)
            {
                float angle = Mathf.Lerp(-halfAngle, halfAngle, (float) i / CIRCLE_STEPS);

                Vector3 current = center + (Quaternion.AngleAxis(angle, Vector3.up) * flat * radius);

                AddSegment(channel, previous, current, color, duration);
                previous = current;
            }
        }

        /// <summary> 월드 좌표에 글자를 붙인다. 값이 어느 물체의 것인지 헷갈리지 않게 한다 </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Label3D(GGizmoChannel channel, Vector3 world, string text, Color color,
                                   float duration = 0f)
        {
            if (!IsCollecting(channel))
            {
                return;
            }

            if (_labelCount >= MAX_LABELS)
            {
                _isOverflowed = true;
                return;
            }

            _labels[_labelCount] = new Label
            {
                World      = world,
                Text       = text ?? string.Empty,
                Color      = color,
                Channel    = channel,
                ExpireTime = (duration > 0f) ? (Time.unscaledTime + duration) : 0f,
                Frame      = Time.frameCount,
            };

            ++_labelCount;

            GGizmoRenderer.EnsureExists();
        }

        /****************************************
        *              Watch · Plot
        ****************************************/

        /// <summary> 이름 붙은 값 한 줄을 기즈모 창에 띄운다. 같은 이름은 덮어쓴다 </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Value(GGizmoChannel channel, string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            float now = Time.unscaledTime;

            for (int i = 0; i < _watches.Count; ++i)
            {
                if (_watches[i].Key != key)
                {
                    continue;
                }

                Watch existing   = _watches[i];
                existing.Value   = value;
                existing.Channel = channel;
                existing.Time    = now;
                _watches[i]      = existing;
                return;
            }

            if (_watches.Count >= MAX_WATCHES)
            {
                _isOverflowed = true;
                return;
            }

            _watches.Add(new Watch
            {
                Key     = key,
                Value   = value,
                Channel = channel,
                Time    = now,
            });

            GGizmoRenderer.EnsureExists();
        }

        /// <summary>
        /// 여러 프레임에 걸쳐 변하는 값을 꺾은선으로 남긴다.
        /// 브레이크포인트로는 "느낌이 이상한" 구간을 못 찾는다, 흔들리는 모양을 봐야 찾는다
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void PlotValue(string key, float value)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            for (int i = 0; i < _plots.Count; ++i)
            {
                if (_plots[i].Key == key)
                {
                    _plots[i].Add(value);
                    return;
                }
            }

            if (_plots.Count >= MAX_PLOTS)
            {
                _isOverflowed = true;
                return;
            }

            var plot = new Plot { Key = key };
            plot.Add(value);
            _plots.Add(plot);

            GGizmoRenderer.EnsureExists();
        }

        /// <summary> 그래프를 지운다 </summary>
        public static void ClearPlots()
        {
            _plots.Clear();
        }

        /****************************************
        *                Reading
        ****************************************/

        /// <summary> 그리는 쪽이 읽는다 </summary>
        public static Segment GetSegment(int index)
        {
            if ((index < 0) || (index >= _segmentCount))
            {
                return default;
            }

            return _segments[index];
        }

        public static Label GetLabel(int index)
        {
            if ((index < 0) || (index >= _labelCount))
            {
                return default;
            }

            return _labels[index];
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 수명이 끝난 것을 버린다. 프레임이 시작될 때 한 번 부른다.
        /// 그린 직후에 버리면 카메라가 두 번 그리는 경우에 두 번째 카메라에서 사라진다
        /// </summary>
        public static void Sweep()
        {
            int   frame = Time.frameCount;
            float now   = Time.unscaledTime;

            int kept = 0;
            for (int i = 0; i < _segmentCount; ++i)
            {
                if (!IsAlive(_segments[i].ExpireTime, _segments[i].Frame, frame, now))
                {
                    continue;
                }

                _segments[kept] = _segments[i];
                ++kept;
            }

            _segmentCount = kept;

            kept = 0;
            for (int i = 0; i < _labelCount; ++i)
            {
                if (!IsAlive(_labels[i].ExpireTime, _labels[i].Frame, frame, now))
                {
                    continue;
                }

                _labels[kept] = _labels[i];
                ++kept;
            }

            _labelCount = kept;

            for (int i = _watches.Count - 1; i >= 0; --i)
            {
                if ((now - _watches[i].Time) > WATCH_LIFETIME)
                {
                    _watches.RemoveAt(i);
                }
            }

            _isOverflowed = false;
        }

        /// <summary> 쌓인 것을 전부 버린다 </summary>
        public static void Clear()
        {
            _segmentCount = 0;
            _labelCount   = 0;
            _watches.Clear();
            _plots.Clear();
            _isOverflowed = false;
        }

        /// <summary> 도메인 리로드를 끈 환경에서 이전 플레이의 선이 남지 않게 한다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            Clear();

            IsEnabled    = true;
            UseDepthTest = false;
            ShowLabels   = true;

            for (int i = 0; i < CHANNEL_COUNT; ++i)
            {
                _isChannelVisible[i] = true;
            }
        }

        private static void AddSegment(GGizmoChannel channel, Vector3 from, Vector3 to, Color color, float duration)
        {
            if (!IsCollecting(channel))
            {
                return;
            }

            if (_segmentCount >= MAX_SEGMENTS)
            {
                _isOverflowed = true;
                return;
            }

            _segments[_segmentCount] = new Segment
            {
                A          = from,
                B          = to,
                Color      = color,
                Channel    = channel,
                ExpireTime = (duration > 0f) ? (Time.unscaledTime + duration) : 0f,
                Frame      = Time.frameCount,
            };

            ++_segmentCount;

            GGizmoRenderer.EnsureExists();
        }

        /// <summary>
        /// 꺼진 갈래는 아예 쌓지 않는다.
        /// 모아 두고 그릴 때 걸러도 되지만, 그러면 꺼 놓아도 판정 루프의 비용이 그대로 남는다
        /// </summary>
        private static bool IsCollecting(GGizmoChannel channel)
        {
            return ((IsEnabled) && (IsChannelVisible(channel)));
        }

        private static bool IsAlive(float expireTime, int bornFrame, int frame, float now)
        {
            // 수명이 없는 것은 태어난 프레임 동안만 산다
            if (expireTime <= 0f)
            {
                return (bornFrame == frame);
            }

            return (expireTime > now);
        }

        /// <summary> 법선만 주어졌을 때 그 평면 위의 두 축을 만든다 </summary>
        private static void GetPlane(Vector3 normal, out Vector3 right, out Vector3 forward)
        {
            if (normal.sqrMagnitude <= 0.0001f)
            {
                normal = Vector3.up;
            }

            normal.Normalize();

            // 법선과 평행한 축을 고르면 외적이 0이 되어 원이 선 하나로 찌그러진다
            Vector3 helper = (Mathf.Abs(normal.y) < 0.99f) ? Vector3.up : Vector3.right;

            right   = Vector3.Cross(normal, helper).normalized;
            forward = Vector3.Cross(normal, right).normalized;
        }

        private static bool[] CreateVisibleChannels()
        {
            var visible = new bool[CHANNEL_COUNT];

            for (int i = 0; i < CHANNEL_COUNT; ++i)
            {
                visible[i] = true;
            }

            return visible;
        }
    }
}
