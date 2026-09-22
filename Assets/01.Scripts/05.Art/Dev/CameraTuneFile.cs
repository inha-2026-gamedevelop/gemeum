// System
using System;
using System.Collections.Generic;
using System.IO;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Data;

namespace Geumeum.Art
{
    /// <summary>
    /// 카메라 조정값을 파일 하나로 주고받는다
    /// 빌드에서 만진 값을 에디터가 읽어 SO에 반영해야 하므로 형식을 한곳에 둔다
    /// </summary>
    public static class CameraTuneFile
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS

        /****************************************
        *                Types
        ****************************************/

        // JsonUtility는 Dictionary를 다루지 못해 목록으로 적는다
        [Serializable]
        private class Entry
        {
            public string Id;
            public float  Value;
        }

        [Serializable]
        private class Payload
        {
            public List<Entry> Values = new List<Entry>();
        }

        /****************************************
        *              Constants
        ****************************************/

        private const string FILE_NAME = "camera-tune.json";

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 빌드에서는 exe 옆, 에디터에서는 프로젝트 루트다 </summary>
        public static string FilePath
        {
            get { return Path.Combine(Directory.GetParent(Application.dataPath).FullName, FILE_NAME); }
        }

        /****************************************
        *              Public API
        ****************************************/

        public static bool TrySave(CameraConfigSO config, IReadOnlyList<CameraConfigSO.Tunable> tunables, out string error)
        {
            error = null;

            if (config == null)
            {
                error = "카메라 설정이 없다";
                return false;
            }

            Payload payload = new Payload();
            for (int i = 0; i < tunables.Count; ++i)
            {
                payload.Values.Add(new Entry { Id = tunables[i].Id, Value = tunables[i].Get(config) });
            }

            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(payload, true));
                return true;
            }
            catch (Exception exception) when ((exception is IOException) || (exception is UnauthorizedAccessException))
            {
                error = exception.Message;
                return false;
            }
        }

        /// <summary> 파일을 읽어 SO에 적용한다. applied는 실제로 반영된 값의 개수다 </summary>
        public static bool TryLoad(CameraConfigSO config, IReadOnlyList<CameraConfigSO.Tunable> tunables, out int applied, out string error)
        {
            applied = 0;
            error   = null;

            if (config == null)
            {
                error = "카메라 설정이 없다";
                return false;
            }

            if (!File.Exists(FilePath))
            {
                error = "파일이 없다: " + FilePath;
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(FilePath);
            }
            catch (Exception exception) when ((exception is IOException) || (exception is UnauthorizedAccessException))
            {
                error = exception.Message;
                return false;
            }

            Payload payload = JsonUtility.FromJson<Payload>(json);
            if ((payload == null) || (payload.Values == null))
            {
                error = "형식을 읽지 못했다";
                return false;
            }

            for (int i = 0; i < payload.Values.Count; ++i)
            {
                Entry entry = payload.Values[i];

                for (int t = 0; t < tunables.Count; ++t)
                {
                    // 파일에만 있고 지금 코드에 없는 Id는 조용히 넘긴다
                    // 값을 지우거나 이름을 바꾼 뒤 옛 파일을 열어도 나머지는 살아야 한다
                    if (tunables[t].Id != entry.Id)
                    {
                        continue;
                    }

                    tunables[t].Set(config, entry.Value);
                    ++applied;
                    break;
                }
            }

            return true;
        }

#endif
    }
}
