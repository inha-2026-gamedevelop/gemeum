// System
using System;
using System.IO;
using System.Linq;

// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using Newtonsoft.Json.Linq;

namespace Geumeum.Art.Editor
{
    /// <summary>Shot list, timing cues and camera pose authoring without recording.</summary>
    public sealed class TeaserPreparationWindow : EditorWindow
    {
        /****************************************
        *                Fields
        ****************************************/

        private TeaserPlanDocument _document;
        private Camera             _camera;
        private int                _selected;
        private int                _frame;
        private Vector2            _scroll;
        private string             _message;

        /****************************************
        *                 Menu
        ****************************************/

        [MenuItem("Geumeum/Art/Teaser Preparation")]
        public static void Open()
        {
            GetWindow<TeaserPreparationWindow>("티저 준비");
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            Reload();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("그믐 · 티저 준비", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("녹화 없이 샷·시간·준비 상태를 확인합니다. 카메라 포즈는 JSON에만 저장하며 씬이나 게임 시간을 바꾸지 않습니다.", MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("새로 읽기"))
            {
                Reload();
            }
            if (GUILayout.Button("JSON 선택"))
            {
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<TextAsset>(TeaserPlanDocument.ASSET_PATH);
            }
            if (GUILayout.Button("사용법"))
            {
                EditorUtility.RevealInFinder(Path.GetFullPath("tools/teaser/README.md"));
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_message))
            {
                EditorGUILayout.HelpBox(_message, (_document == null) ? MessageType.Error : MessageType.Info);
            }
            if (_document == null)
            {
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            JObject root = _document.Root;
            EditorGUILayout.LabelField($"{root["totalFrames"]} frames / {root["fps"]}fps · {_document.Shots.Count}샷");
            string[] labels = _document.Shots.Select(s => $"{s["id"]} · {s["label"]}").ToArray();
            _selected = EditorGUILayout.Popup("샷", _selected, labels);
            JObject shot = (JObject)_document.Shots[_selected];
            int length = (int)shot["durationFrames"];
            _frame = EditorGUILayout.IntSlider("샷 내부 프레임", Mathf.Clamp(_frame, 0, length - 1), 0, length - 1);
            int outputFrame = (int)shot["startFrame"] + _frame;
            EditorGUILayout.LabelField($"전체 {outputFrame}f · {((float)outputFrame / TeaserPlanDocument.OUTPUT_FPS):F3}초 · {shot["kind"]}");
            EditorGUILayout.LabelField("씬", (string)shot["scene"]);
            EditorGUILayout.LabelField("초기 배치", (string)shot["setupNote"], EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("카메라 포즈", EditorStyles.boldLabel);
            _camera = (Camera)EditorGUILayout.ObjectField("참조 카메라", _camera, typeof(Camera), true);
            bool canSave = (!EditorApplication.isPlaying) && (_camera != null) && (!_camera.orthographic) &&
                (_camera.gameObject.scene.path == (string)shot["scene"]) &&
                (!string.IsNullOrEmpty((string)shot["scene"]));
            using (new EditorGUI.DisabledScope(!canSave))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("현재 포즈 → 시작"))
                {
                    SavePose(true);
                }
                if (GUILayout.Button("현재 포즈 → 종료"))
                {
                    SavePose(false);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.HelpBox("같은 씬의 Perspective 카메라를 지정하면 편집 모드에서 저장할 수 있습니다. 현재 카메라를 움직이거나 씬을 자동으로 열지 않습니다.", MessageType.None);
            EditorGUILayout.LabelField("시작", PoseLabel(shot["cameraFrom"]));
            EditorGUILayout.LabelField("종료", PoseLabel(shot["cameraTo"]));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("현재 프레임의 편집·소리 지시", EditorStyles.boldLabel);
            foreach (JToken cue in ((JArray)root["overlays"]).Concat((JArray)root["audioCues"]))
            {
                if ((outputFrame >= (int)cue["startFrame"]) &&
                    (outputFrame < (int)cue["startFrame"] + (int)cue["durationFrames"]))
                {
                    EditorGUILayout.LabelField((string)(cue["text"] ?? cue["label"]), EditorStyles.wordWrappedLabel);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("준비 항목", EditorStyles.boldLabel);
            foreach (JToken requirement in (JArray)root["requirements"])
            {
                EditorGUILayout.HelpBox($"{requirement["id"]} [{requirement["status"]}]\n{requirement["note"]}",
                    ((string)requirement["status"] == "blocked") ? MessageType.Warning : MessageType.None);
            }
            EditorGUILayout.EndScrollView();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Reload()
        {
            try
            {
                _document = TeaserPlanDocument.Load(TeaserPlanDocument.ASSET_PATH);
                _selected = Mathf.Clamp(_selected, 0, _document.Shots.Count - 1);
                _message  = "샷 구간 검증 통과. 전체 데이터 검증: tools/teaser에서 npm.cmd run validate";
            }
            catch (Exception exception)
            {
                _document = null;
                _message  = exception.Message;
            }
        }

        private void SavePose(bool start)
        {
            try
            {
                _document.SavePose(_selected, start, _camera.transform.position,
                    _camera.transform.eulerAngles, _camera.fieldOfView, _camera.gameObject.scene.path);
                AssetDatabase.ImportAsset(TeaserPlanDocument.ASSET_PATH);
                _message = "포즈 저장 완료. 씬은 변경하지 않았습니다.";
            }
            catch (Exception exception)
            {
                _message = exception.Message;
            }
        }

        private static string PoseLabel(JToken pose)
        {
            return ((pose == null) || (pose.Type == JTokenType.Null))
                ? "미설정"
                : pose.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
