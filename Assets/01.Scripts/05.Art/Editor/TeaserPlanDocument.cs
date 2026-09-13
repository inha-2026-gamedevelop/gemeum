// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Unity
using UnityEngine;

// 서드파티
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Geumeum.Art.Editor
{
    /// <summary>Editor-only shot planning document. Does not control gameplay or recording.</summary>
    public sealed class TeaserPlanDocument
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string  ASSET_PATH        = "Assets/09.Data/Teaser/teaser-shots.json";
        public const int     OUTPUT_FPS        = 60;
        private const int    SCHEMA_VERSION    = 1;
        private const int    OUTPUT_WIDTH      = 1920;
        private const int    OUTPUT_HEIGHT     = 1080;
        private const int    MIN_FRAMES        = 4320;
        private const int    MAX_FRAMES        = 4380;
        private const float  MAX_FOV           = 180f;
        private const double NORMAL_TIME_SCALE = 1d;

        private readonly string _path;
        private string          _sourceText;

        /****************************************
        *              Properties
        ****************************************/

        public JObject Root  { get; private set; }
        public JArray  Shots => (JArray)Root["shots"];

        /****************************************
        *              Constructors
        ****************************************/

        private TeaserPlanDocument(string path, string text, JObject root)
        {
            _path       = path;
            _sourceText = text;
            Root        = root;
        }

        /****************************************
        *                Methods
        ****************************************/

        public static TeaserPlanDocument Load(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            JObject root = Parse(text);
            return new TeaserPlanDocument(path, text, root);
        }

        public static JObject Parse(string text)
        {
            JObject root = JObject.Parse(text);
            if ((ReadInteger(root, "schemaVersion") != SCHEMA_VERSION) ||
                (ReadInteger(root, "fps") != OUTPUT_FPS) ||
                (ReadInteger(root, "width") != OUTPUT_WIDTH) ||
                (ReadInteger(root, "height") != OUTPUT_HEIGHT))
            {
                throw new InvalidDataException("schemaVersion 1 / 60fps 데이터가 필요합니다.");
            }

            int total = ReadInteger(root, "totalFrames");
            if ((total < MIN_FRAMES) || (total > MAX_FRAMES) ||
                (!(root["shots"] is JArray shots)) || (shots.Count == 0))
            {
                throw new InvalidDataException("72~73초의 샷 목록이 필요합니다.");
            }

            long end = 0;
            HashSet<string> ids = new HashSet<string>();
            foreach (JToken token in shots)
            {
                if (!(token is JObject shot))
                {
                    throw new InvalidDataException("빈 샷이 있습니다.");
                }

                string id = (string)shot["id"];
                int start = ReadInteger(shot, "startFrame");
                int duration = ReadInteger(shot, "durationFrames");
                if ((string.IsNullOrWhiteSpace(id)) || (!ids.Add(id)) || (start != end) || (duration <= 0))
                {
                    throw new InvalidDataException($"샷 ID 또는 시간 구간이 잘못됐습니다: {id}");
                }

                if (((string)shot["kind"] == "gameplay") && ((double?)shot["timeScale"] != NORMAL_TIME_SCALE))
                {
                    throw new InvalidDataException($"전투 샷은 정상 배속으로 준비합니다: {id}");
                }

                end = (long)start + duration;
            }

            if (end != total)
            {
                throw new InvalidDataException("샷 길이 합계와 totalFrames가 다릅니다.");
            }

            foreach (string name in new[] { "overlays", "audioCues", "requirements" })
            {
                if (!(root[name] is JArray items))
                {
                    throw new InvalidDataException($"목록이 필요합니다: {name}");
                }
                foreach (JToken token in items)
                {
                    if (!(token is JObject item))
                    {
                        throw new InvalidDataException($"빈 항목이 있습니다: {name}");
                    }
                    if (name != "requirements")
                    {
                        int start = ReadInteger(item, "startFrame");
                        int duration = ReadInteger(item, "durationFrames");
                        if ((start < 0) || (duration <= 0) || ((long)start + duration > total))
                        {
                            throw new InvalidDataException($"구간이 잘못됐습니다: {name}");
                        }
                    }
                }
            }

            return root;
        }

        public void SavePose(int index, bool start, Vector3 position, Vector3 euler,
            float fov, string scenePath)
        {
            if ((index < 0) || (index >= Shots.Count) || (!Finite(position)) || (!Finite(euler)) ||
                (float.IsNaN(fov)) || (float.IsInfinity(fov)) || (fov <= 0f) || (fov >= MAX_FOV))
            {
                throw new ArgumentException("카메라 포즈가 유효하지 않습니다.");
            }

            if (((string)Shots[index]["scene"] != scenePath) || (string.IsNullOrEmpty(scenePath)))
            {
                throw new InvalidOperationException("선택한 샷과 카메라의 씬이 다릅니다.");
            }

            JObject changed = (JObject)Root.DeepClone();
            changed["shots"][index][start ? "cameraFrom" : "cameraTo"] = new JObject
            {
                ["position"] = new JArray(position.x, position.y, position.z),
                ["euler"]    = new JArray(euler.x, euler.y, euler.z),
                ["fov"]      = fov
            };
            string output = changed.ToString(Formatting.Indented) + "\n";
            Parse(output);
            // Refuse to overwrite another editor's changes since this window loaded the file.
            if (File.ReadAllText(_path, Encoding.UTF8) != _sourceText)
            {
                throw new IOException("파일이 다른 곳에서 바뀌었습니다. 새로 읽은 뒤 저장하세요.");
            }

            string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, output, new UTF8Encoding(false));
                File.Replace(temporary, _path, null);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }

            Root        = changed;
            _sourceText = output;
        }

        private static int ReadInteger(JObject root, string name)
        {
            JToken token = root[name];
            if ((token == null) || (token.Type != JTokenType.Integer))
            {
                throw new InvalidDataException($"정수 프레임 필드가 필요합니다: {name}");
            }

            return token.Value<int>();
        }

        private static bool Finite(Vector3 vector)
        {
            return (!float.IsNaN(vector.x)) && (!float.IsInfinity(vector.x)) &&
                (!float.IsNaN(vector.y)) && (!float.IsInfinity(vector.y)) &&
                (!float.IsNaN(vector.z)) && (!float.IsInfinity(vector.z));
        }
    }
}
