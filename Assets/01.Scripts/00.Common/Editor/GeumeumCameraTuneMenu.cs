// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Geumeum.Art;
using Geumeum.Data;

namespace Geumeum.EditorTools
{
    /// <summary>
    /// 빌드에서 만진 카메라 조정값을 정본 SO에 반영한다.
    /// 빌드 안의 값은 프로세스가 끝나면 사라지므로, 파일을 거쳐 여기서 확정해야 팀에 남는다
    /// </summary>
    public static class GeumeumCameraTuneMenu
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string MENU_CAMERA = GeumeumBuildMenu.MENU_ROOT + "카메라/";

        private const int ORDER_APPLY = 0;
        private const int ORDER_WRITE = 1;
        private const int ORDER_PATH  = 20;

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem(MENU_CAMERA + "조정값 파일을 SO에 반영", false, ORDER_APPLY)]
        private static void ApplyFileToAsset()
        {
            CameraConfigSO config = FindConfig();
            if (config == null)
            {
                return;
            }

            IReadOnlyList<CameraConfigSO.Tunable> tunables = CameraConfigSO.Tunables;

            if (!CameraTuneFile.TryLoad(config, tunables, out int applied, out string error))
            {
                EditorUtility.DisplayDialog("카메라 조정값", "불러오지 못했다.\n\n" + error, "확인");
                return;
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            Debug.Log($"[GeumeumCameraTuneMenu] {applied}개를 {AssetDatabase.GetAssetPath(config)}에 반영했다");
            EditorUtility.DisplayDialog("카메라 조정값", $"{applied}개를 SO에 반영했다.\n\n{CameraTuneFile.FilePath}", "확인");
        }

        [MenuItem(MENU_CAMERA + "지금 SO 값을 조정값 파일로 저장", false, ORDER_WRITE)]
        private static void WriteAssetToFile()
        {
            CameraConfigSO config = FindConfig();
            if (config == null)
            {
                return;
            }

            if (!CameraTuneFile.TrySave(config, CameraConfigSO.Tunables, out string error))
            {
                EditorUtility.DisplayDialog("카메라 조정값", "저장하지 못했다.\n\n" + error, "확인");
                return;
            }

            EditorUtility.DisplayDialog("카메라 조정값", "저장했다.\n\n" + CameraTuneFile.FilePath, "확인");
        }

        [MenuItem(MENU_CAMERA + "조정값 파일 위치 보기", false, ORDER_PATH)]
        private static void ShowPath()
        {
            Debug.Log("[GeumeumCameraTuneMenu] 조정값 파일: " + CameraTuneFile.FilePath);
            EditorUtility.RevealInFinder(CameraTuneFile.FilePath);
        }

        /// <summary> 프로젝트에 하나뿐인 카메라 설정을 찾는다. 둘 이상이면 어느 쪽이 정본인지 알 수 없어 멈춘다 </summary>
        private static CameraConfigSO FindConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(CameraConfigSO));

            if (guids.Length == 0)
            {
                EditorUtility.DisplayDialog("카메라 조정값", "CameraConfigSO 에셋을 찾지 못했다.", "확인");
                return null;
            }

            if (guids.Length > 1)
            {
                EditorUtility.DisplayDialog("카메라 조정값",
                    $"CameraConfigSO가 {guids.Length}개다. 어느 쪽이 정본인지 정한 뒤 다시 실행할 것.", "확인");
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<CameraConfigSO>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
