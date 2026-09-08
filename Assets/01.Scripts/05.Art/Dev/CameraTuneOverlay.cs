// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Art
{
    /// <summary>
    /// 게임을 껐다 켜지 않고 카메라 값을 만지는 개발용 패널.
    /// 값이 바뀌면 CameraRig가 다음 프레임에 그대로 읽으므로 화면에서 즉시 확인된다
    /// </summary>
    public class CameraTuneOverlay : PersistentSingleton<CameraTuneOverlay>
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

        private const string OBJECT_NAME = "@CameraTuneOverlay";

        private const int PANEL_WIDTH   = 470;
        private const int PANEL_MARGIN  = 10;
        private const int LABEL_WIDTH   = 250;
        private const int FIELD_WIDTH   = 60;
        private const int ROW_HEIGHT    = 40; // 설명 줄 + 슬라이더 줄
        private const int HEADER_HEIGHT = 96; // 제목, 버튼 한 줄, 안내 한 줄

        private const float GROUP_SPACING = 6f; // 그룹 사이 여백

        private const string VALUE_FORMAT = "0.###";

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private KeyCode _toggleKey = KeyCode.F11;

        private bool    _isOpen  = false;
        private Vector2 _scroll  = Vector2.zero;
        private string  _message = string.Empty;

        // 칸에 입력 중인 문자열. null이면 현재 값에서 다시 만든다. 인덱스는 Tunables와 같다
        private string[] _editing = null;

        // 패널을 처음 연 시점의 값. [처음 값으로]가 여기로 되돌린다
        private float[] _startValue = null;

        /****************************************
        *              Unity Event
        ****************************************/

        /// <summary> 씬이나 프리팹에 배치하지 않아도 스스로 생긴다. 씬 파일을 건드리지 않기 위한 것이다 </summary>
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
            if (!Input.GetKeyDown(_toggleKey))
            {
                return;
            }

            _isOpen = !_isOpen;

            if (_isOpen)
            {
                CaptureStartValues();
            }
        }

        private void OnGUI()
        {
            if (!_isOpen)
            {
                return;
            }

            CameraConfigSO config = ResolveConfig();
            if (config == null)
            {
                return;
            }

            IReadOnlyList<CameraConfigSO.Tunable> tunables = CameraConfigSO.Tunables;
            EnsureBuffers(tunables.Count);

            float wanted = HEADER_HEIGHT + (tunables.Count * ROW_HEIGHT);
            float height = Mathf.Min(Screen.height - (PANEL_MARGIN * 2), wanted);

            GUILayout.BeginArea(new Rect(PANEL_MARGIN, PANEL_MARGIN, PANEL_WIDTH, height), GUI.skin.box);

            DrawHeader(config, tunables);

            _scroll = GUILayout.BeginScrollView(_scroll);
            DrawRows(config, tunables);
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void DrawHeader(CameraConfigSO config, IReadOnlyList<CameraConfigSO.Tunable> tunables)
        {
            GUILayout.Label($"카메라 조정 — {_toggleKey}로 닫는다");

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("처음 값으로"))
            {
                RestoreStartValues(config, tunables);
            }

            if (GUILayout.Button("파일로 저장"))
            {
                Save(config, tunables);
            }

            if (GUILayout.Button("파일에서 불러오기"))
            {
                Load(config, tunables);
            }

            GUILayout.EndHorizontal();

            GUILayout.Label(string.IsNullOrEmpty(_message) ? CameraTuneFile.FilePath : _message);
        }

        private void DrawRows(CameraConfigSO config, IReadOnlyList<CameraConfigSO.Tunable> tunables)
        {
            string group = null;

            for (int i = 0; i < tunables.Count; ++i)
            {
                CameraConfigSO.Tunable tunable = tunables[i];

                if (tunable.Group != group)
                {
                    group = tunable.Group;
                    GUILayout.Space(GROUP_SPACING);
                    GUILayout.Label("— " + group + " —");
                }

                DrawRow(config, tunable, i);
            }
        }

        /// <summary> 한 줄이 문장 하나가 되게 그린다. 변수 이름을 따로 설명할 필요가 없어야 한다 </summary>
        private void DrawRow(CameraConfigSO config, CameraConfigSO.Tunable tunable, int index)
        {
            float current = tunable.Get(config);

            if (_editing[index] == null)
            {
                _editing[index] = current.ToString(VALUE_FORMAT);
            }

            GUILayout.BeginHorizontal();

            GUILayout.Label(tunable.Before, GUILayout.Width(LABEL_WIDTH));

            string typed = GUILayout.TextField(_editing[index], GUILayout.Width(FIELD_WIDTH));
            if (typed != _editing[index])
            {
                _editing[index] = typed;

                // 타이핑 도중에는 숫자가 아닐 수 있다. 그때는 값을 건드리지 않고 칸만 둔다
                if (float.TryParse(typed, out float parsed))
                {
                    tunable.Set(config, parsed);
                    current = parsed;
                }
            }

            GUILayout.Label(tunable.After);

            GUILayout.EndHorizontal();

            float moved = GUILayout.HorizontalSlider(current, tunable.Min, tunable.Max);
            if (!Mathf.Approximately(moved, current))
            {
                tunable.Set(config, moved);
                _editing[index] = moved.ToString(VALUE_FORMAT);
            }
        }

        private static CameraConfigSO ResolveConfig()
        {
            return GameDB.IsInitialized ? GameDB.Camera : null;
        }

        private void EnsureBuffers(int count)
        {
            if ((_editing != null) && (_editing.Length == count))
            {
                return;
            }

            _editing = new string[count];
        }

        private void CaptureStartValues()
        {
            // 처음 연 시점의 값만 기억한다. 두 번째로 열 때 덮어쓰면 되돌릴 자리를 잃는다
            if (_startValue != null)
            {
                return;
            }

            CameraConfigSO config = ResolveConfig();
            if (config == null)
            {
                return;
            }

            IReadOnlyList<CameraConfigSO.Tunable> tunables = CameraConfigSO.Tunables;

            _startValue = new float[tunables.Count];
            for (int i = 0; i < tunables.Count; ++i)
            {
                _startValue[i] = tunables[i].Get(config);
            }
        }

        private void RestoreStartValues(CameraConfigSO config, IReadOnlyList<CameraConfigSO.Tunable> tunables)
        {
            if (_startValue == null)
            {
                return;
            }

            for (int i = 0; i < tunables.Count; ++i)
            {
                tunables[i].Set(config, _startValue[i]);
                _editing[i] = null;
            }

            _message = "패널을 처음 연 시점의 값으로 되돌렸다";
        }

        private void Save(CameraConfigSO config, IReadOnlyList<CameraConfigSO.Tunable> tunables)
        {
            if (CameraTuneFile.TrySave(config, tunables, out string error))
            {
                _message = "저장했다: " + CameraTuneFile.FilePath;
                return;
            }

            _message = "저장 실패: " + error;
            Debug.LogError($"[CameraTuneOverlay] 저장 실패: {error}", this);
        }

        private void Load(CameraConfigSO config, IReadOnlyList<CameraConfigSO.Tunable> tunables)
        {
            if (!CameraTuneFile.TryLoad(config, tunables, out int applied, out string error))
            {
                _message = "불러오기 실패: " + error;
                return;
            }

            for (int i = 0; i < _editing.Length; ++i)
            {
                _editing[i] = null;
            }

            _message = $"{applied}개를 불러왔다";
        }

#endif
    }
}
