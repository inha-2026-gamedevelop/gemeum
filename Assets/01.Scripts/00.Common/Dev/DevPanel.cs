// System
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Geumeum.Common
{
    /// <summary> 브라우저 패널에 그릴 조작 하나의 종류 </summary>
    public enum DevControlKind
    {
        Label  = 0, // 읽기만 하는 한 줄
        Toggle = 1,
        Button = 2,
        Slider = 3,
        Text   = 4, // 숫자나 문자를 직접 입력한다
        Choice = 5, // 여럿 중 하나를 고른다
        Log    = 6, // 줄이 쌓이는 목록
        Plot   = 7, // 여러 프레임에 걸친 값의 꺾은선
    }

    /// <summary>
    /// 조작 하나를 브라우저로 보낼 형태.
    /// JsonUtility는 상속을 직렬화하지 못하므로 종류마다 클래스를 나누지 않고 한 형태에 다 담는다
    /// </summary>
    [Serializable]
    public class DevControlView
    {
        public string   kind;
        public string   id;
        public string   label;
        public string   text;
        public bool     isOn;
        public float    value;
        public float    min;
        public float    max;
        public string[] options;
        public string[] lines;
        public float[]  samples;
    }

    /// <summary> 패널 하나 </summary>
    [Serializable]
    public class DevPanelView
    {
        public string           id;
        public string           title;
        public DevControlView[] controls;
    }

    /// <summary> 한 번에 브라우저로 보내는 전체 상태 </summary>
    [Serializable]
    public class DevSnapshotView
    {
        public string         product;
        public string         role;    // 호스트 · 클라이언트처럼 이 프로세스가 무엇인지
        public int            revision;
        public DevPanelView[] panels;
    }

    /// <summary>
    /// 패널이 자기 조작을 쌓는 곳.
    /// 각 개발 도구는 GUILayout을 부르는 대신 여기에 무엇이 있는지만 적는다 -
    /// 그리는 쪽이 게임 화면이 아니라 브라우저라 그리기 코드를 들고 있을 수 없다
    /// </summary>
    public sealed class DevPanelBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly List<DevControlView> _controls = new List<DevControlView>(32);

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 스냅샷을 만들 때마다 비우고 다시 채운다 </summary>
        public void Clear()
        {
            _controls.Clear();
        }

        public DevControlView[] ToArray()
        {
            return _controls.ToArray();
        }

        /// <summary> 읽기만 하는 한 줄 </summary>
        public void Label(string text)
        {
            Add(DevControlKind.Label, null, null, text);
        }

        /// <summary> 구역을 나누는 제목 </summary>
        public void Header(string text)
        {
            Add(DevControlKind.Label, null, "header", text);
        }

        public void Toggle(string id, string label, bool isOn)
        {
            DevControlView control = Add(DevControlKind.Toggle, id, label, null);
            control.isOn = isOn;
        }

        public void Button(string id, string label)
        {
            Add(DevControlKind.Button, id, label, null);
        }

        public void Slider(string id, string label, float value, float min, float max)
        {
            DevControlView control = Add(DevControlKind.Slider, id, label, FormatNumber(value));
            control.value = value;
            control.min   = min;
            control.max   = max;
        }

        public void Text(string id, string label, string value)
        {
            Add(DevControlKind.Text, id, label, value);
        }

        /// <summary> 여럿 중 하나. selected는 options 중 하나와 같아야 눌린 것으로 보인다 </summary>
        public void Choice(string id, string label, string selected, params string[] options)
        {
            DevControlView control = Add(DevControlKind.Choice, id, label, selected);
            control.options = options;
        }

        /// <summary> 줄 목록. 각 줄을 "성격|내용"으로 넣으면 브라우저가 색을 나눈다 </summary>
        public void Log(string id, string[] lines)
        {
            DevControlView control = Add(DevControlKind.Log, id, null, null);
            control.lines = lines;
        }

        /// <summary>
        /// 앞의 count줄만 보낸다.
        /// 태그로 걸러 낸 줄을 담을 때 배열은 그대로 두고 채운 만큼만 쓰기 위한 것이다 -
        /// 스냅샷마다 배열을 새로 만들면 로그가 쌓일수록 GC가 돈다
        /// </summary>
        public void Log(string id, string[] lines, int count)
        {
            DevControlView control = Add(DevControlKind.Log, id, null, null);

            if ((lines == null) || (count <= 0))
            {
                control.lines = Array.Empty<string>();
                return;
            }

            int length = (count < lines.Length) ? count : lines.Length;

            var trimmed = new string[length];
            Array.Copy(lines, trimmed, length);

            control.lines = trimmed;
        }

        public void Plot(string id, string label, float[] samples, float min, float max)
        {
            DevControlView control = Add(DevControlKind.Plot, id, label, null);
            control.samples = samples;
            control.min     = min;
            control.max     = max;
        }

        private DevControlView Add(DevControlKind kind, string id, string label, string text)
        {
            var control = new DevControlView
            {
                kind  = kind.ToString().ToLowerInvariant(),
                id    = id,
                label = label,
                text  = text,
            };

            _controls.Add(control);
            return control;
        }

        private static string FormatNumber(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }

    /// <summary> 브라우저에서 온 조작 하나 </summary>
    public struct DevCommand
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 어느 패널의 조작인지 </summary>
        public string Panel { get; }

        /// <summary> 어느 조작인지. 패널이 자기 조작에 붙인 id다 </summary>
        public string Control { get; }

        /// <summary> 브라우저가 보낸 값. 버튼은 빈 문자열이다 </summary>
        public string Value { get; }

        /// <summary> 토글 값 </summary>
        public bool IsOn
        {
            get
            {
                return ((Value == "1") || (string.Equals(Value, "true", StringComparison.OrdinalIgnoreCase)));
            }
        }

        /****************************************
        *              Constructor
        ****************************************/

        public DevCommand(string panel, string control, string value)
        {
            Panel   = panel;
            Control = control;
            Value   = value ?? string.Empty;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 숫자 값. 브라우저는 늘 점을 소수점으로 보내므로 문화권에 기대지 않는다 </summary>
        public bool TryGetFloat(out float number)
        {
            return float.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }

        public bool TryGetInt(out int number)
        {
            return int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
        }
    }
}
