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
    /// 값이 바뀌면 CameraRig가 다음 프레임에 그대로 읽으므로 화면에서 즉시 확인된다.
    ///
    /// 조작은 브라우저 개발 패널에 있다 - 카메라를 보면서 만져야 하는데
    /// 게임 화면 위에 슬라이더를 깔면 정작 봐야 할 화면을 덮는다
    /// </summary>
    public class CameraTuneOverlay : PersistentSingleton<CameraTuneOverlay>
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD

        /****************************************
        *                Preload
        ****************************************/

        /// <summary>
        /// 씬에 배치하지 않아도 실행 시작에 스스로 뜬다.
        /// EnsureCreated가 기존 인스턴스와 씬 배치본을 먼저 확인하므로 중복이 생기지 않는다
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Preload()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            EnsureCreated(OBJECT_NAME);
        }

        /****************************************
        *              Constants
        ****************************************/

        private const string OBJECT_NAME = "@CameraTuneOverlay";

        private const string PANEL_ID    = "camera";
        private const string PANEL_TITLE = "카메라 조정";

        /****************************************
        *                Fields
        ****************************************/

        private string _message = string.Empty;

        // 패널을 처음 만들 때의 값. [처음 값으로]가 여기로 되돌린다
        private float[] _startValue = null;

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
            CameraConfigSO config = ResolveConfig();
            if (config == null)
            {
                builder.Label("아직 GameDB가 올라오지 않았다");
                return;
            }

            IReadOnlyList<CameraConfigSO.Tunable> tunables = CameraConfigSO.Tunables;

            // 처음 값은 패널을 처음 만들 때 잡는다. GameDB가 늦게 올라오므로 Awake에서는 아직 없다
            CaptureStartValues();

            builder.Button("reset", "처음 값으로");
            builder.Button("save",  "파일로 저장");
            builder.Button("load",  "파일에서 불러오기");

            builder.Label(string.IsNullOrEmpty(_message) ? CameraTuneFile.FilePath : _message);

            string group = null;

            for (int i = 0; i < tunables.Count; ++i)
            {
                CameraConfigSO.Tunable tunable = tunables[i];

                if (tunable.Group != group)
                {
                    group = tunable.Group;
                    builder.Header(group);
                }

                // 한 줄이 문장 하나가 되게 적는다. 변수 이름을 따로 설명할 필요가 없어야 한다
                string label = tunable.Before + " __ " + tunable.After;

                builder.Slider("tune." + i, label, tunable.Get(config), tunable.Min, tunable.Max);
            }
        }

        private void Handle(DevCommand command)
        {
            CameraConfigSO config = ResolveConfig();
            if (config == null)
            {
                return;
            }

            IReadOnlyList<CameraConfigSO.Tunable> tunables = CameraConfigSO.Tunables;

            switch (command.Control)
            {
                case "reset":
                    RestoreStartValues(config, tunables);
                    return;

                case "save":
                    Save(config, tunables);
                    return;

                case "load":
                    Load(config, tunables);
                    return;
            }

            if ((string.IsNullOrEmpty(command.Control)) || (!command.Control.StartsWith("tune.")))
            {
                return;
            }

            if (!int.TryParse(command.Control.Substring("tune.".Length), out int index))
            {
                return;
            }

            if ((index < 0) || (index >= tunables.Count))
            {
                return;
            }

            if (!command.TryGetFloat(out float value))
            {
                return;
            }

            // 슬라이더 밖의 값이 들어오면 이후 슬라이더 위치와 실제 값이 어긋난다
            CameraConfigSO.Tunable tunable = tunables[index];
            tunable.Set(config, Mathf.Clamp(value, tunable.Min, tunable.Max));
        }

        /****************************************
        *                Methods
        ****************************************/

        private static CameraConfigSO ResolveConfig()
        {
            return GameDB.IsInitialized ? GameDB.Camera : null;
        }

        private void CaptureStartValues()
        {
            // 처음 값만 기억한다. 나중에 덮어쓰면 되돌릴 자리를 잃는다
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

            for (int i = 0; (i < tunables.Count) && (i < _startValue.Length); ++i)
            {
                tunables[i].Set(config, _startValue[i]);
            }

            _message = "패널을 처음 만든 시점의 값으로 되돌렸다";
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

            _message = $"{applied}개를 불러왔다";
        }

#endif
    }
}
