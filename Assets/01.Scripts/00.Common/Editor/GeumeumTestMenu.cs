// Unity
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Geumeum.EditorTools
{
    /// <summary>
    /// 툴바의 그믐 - 테스트 메뉴
    /// Test Runner 창을 열고 목록에서 찾아 돌리던 것을 메뉴 한 번으로 줄인다
    /// 결과는 콘솔에 요약으로 남는다 - 창을 열어 두지 않아도 통과 여부를 알 수 있어야 한다
    /// </summary>
    public static class GeumeumTestMenu
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string MENU_TEST = GeumeumBuildMenu.MENU_ROOT + "테스트/";

        private const string TEST_RUNNER_MENU = "Window/General/Test Runner";

        // 테스트가 끝나면 도메인 리로드가 콘솔을 비운다. 요약을 세션에 적어 두고 리로드 뒤에 다시 찍는다
        private const string SESSION_KEY_SUMMARY = "Geumeum.TestSummary";
        private const string SESSION_KEY_FAILED  = "Geumeum.TestFailed";

        private const int ORDER_RUN_EDIT_MODE = 0;
        private const int ORDER_OPEN_RUNNER   = 20;

        // CreateInstance로 만든 ScriptableObject는 참조를 놓으면 수거된다
        // 그때 등록한 콜백까지 같이 사라져서 결과가 콘솔에 안 남는다 - 정적으로 붙들어 둔다
        private static TestRunnerApi G_Api;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> EditMode 전체를 돌린다. 코드 변경 뒤 L1을 넘겼는지 보는 가장 싼 확인이다 </summary>
        [MenuItem(MENU_TEST + "EditMode 테스트 실행", false, ORDER_RUN_EDIT_MODE)]
        private static void RunEditModeTests()
        {
            ResolveApi().Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
        }

        private static TestRunnerApi ResolveApi()
        {
            if (G_Api != null)
            {
                return G_Api;
            }

            G_Api = ScriptableObject.CreateInstance<TestRunnerApi>();

            // 씬 전환과 GC에서 살아남아야 RunFinished까지 콜백이 붙어 있다
            G_Api.hideFlags = HideFlags.HideAndDontSave;
            G_Api.RegisterCallbacks(new Callbacks());

            return G_Api;
        }

        [MenuItem(MENU_TEST + "Test Runner 열기", false, ORDER_OPEN_RUNNER)]
        private static void OpenTestRunner()
        {
            EditorApplication.ExecuteMenuItem(TEST_RUNNER_MENU);
        }

        /****************************************
        *                Types
        ****************************************/

        /// <summary>
        /// 아직 찍지 않은 요약을 콘솔에 남긴다. 실행 직후의 콘솔 비우기와 도메인 리로드가
        /// 결과를 조용히 삼키기 때문에, 세션에 적어 두고 안전한 시점에 한 번만 찍는다
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ReportPendingSummary()
        {
            string summary = SessionState.GetString(SESSION_KEY_SUMMARY, string.Empty);
            if (string.IsNullOrEmpty(summary))
            {
                return;
            }

            bool failed = SessionState.GetBool(SESSION_KEY_FAILED, false);

            SessionState.EraseString(SESSION_KEY_SUMMARY);
            SessionState.EraseBool(SESSION_KEY_FAILED);

            if (failed)
            {
                Debug.LogError(summary);
                return;
            }

            Debug.Log(summary);
        }

        /// <summary> 실행 결과를 콘솔 한 줄로 요약한다. 개별 테스트는 Test Runner 창에서 본다 </summary>
        private sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                string summary =
                    $"[그믐] EditMode 테스트: 통과 {result.PassCount} · 실패 {result.FailCount} · " +
                    $"건너뜀 {result.SkipCount} ({result.Duration:F2}초)";

                bool failed = (result.FailCount > 0);

                // 리로드가 콘솔을 비우면 지금 찍은 것이 사라진다. 세션에 남겨 두고 리로드 뒤에 다시 찍는다
                SessionState.SetString(SESSION_KEY_SUMMARY, summary);
                SessionState.SetBool(SESSION_KEY_FAILED, failed);

                // 지금 찍으면 실행 직후의 콘솔 비우기에 같이 지워진다
                // 한 틱 뒤에 찍고, 도메인 리로드가 끼면 InitializeOnLoadMethod가 대신 찍는다
                // 어느 쪽이든 키를 지우므로 두 번 찍히지 않는다
                EditorApplication.delayCall += ReportPendingSummary;
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }
        }
    }
}
