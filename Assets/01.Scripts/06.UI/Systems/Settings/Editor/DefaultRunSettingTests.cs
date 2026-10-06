// System
using System.Reflection;

// Third party
using NUnit.Framework;
using TMPro;

// Unity
using UnityEngine;

// Project
using Geumeum.Common;

namespace Geumeum.UI.Editor
{
    /// <summary>
    /// 기본 달리기 설정. 키가 없는 기존 사용자는 기본 달리기로 시작하고, 직접 끈 값은 유지되며,
    /// 설정 창 게임플레이 목록에 켜기/끄기 줄이 생겨 실제로 저장까지 이어지는지 본다
    /// </summary>
    public sealed class DefaultRunSettingTests
    {
        // LocalMovementPreferences의 PlayerPrefs 키. 바뀌면 기존 사용자 설정도 사라지므로 여기서 함께 붙잡는다
        private const string KEY_DEFAULT_RUN = "Input.DefaultRun";
        private const string PREFAB_PATH     = "Assets/02.Prefabs/UI/RunManagementUI.prefab";
        private const string ROW_NAME        = "DefaultRunRow";

        private const BindingFlags PRIVATE = BindingFlags.Instance | BindingFlags.NonPublic;

        private bool _hadKey;
        private int  _savedValue;

        [SetUp]
        public void SetUp()
        {
            // 에디터의 실제 설정을 건드리므로 원래 값을 남겨 두고 끝나면 되돌린다
            _hadKey     = PlayerPrefs.HasKey(KEY_DEFAULT_RUN);
            _savedValue = PlayerPrefs.GetInt(KEY_DEFAULT_RUN, 1);
            PlayerPrefs.DeleteKey(KEY_DEFAULT_RUN);
            LocalMovementPreferences.Reload();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadKey)
            {
                PlayerPrefs.SetInt(KEY_DEFAULT_RUN, _savedValue);
            }
            else
            {
                PlayerPrefs.DeleteKey(KEY_DEFAULT_RUN);
            }

            LocalMovementPreferences.Reload();
        }

        [Test]
        public void MissingKeyStartsWithDefaultRunAndDoesNotWriteIt()
        {
            Assert.IsTrue(LocalMovementPreferences.DefaultRun);

            // 읽기만으로 기본값을 저장해 버리면 다음 기본값 변경을 따라가지 못한다
            Assert.IsFalse(PlayerPrefs.HasKey(KEY_DEFAULT_RUN));
        }

        [Test]
        public void SavedOffFromEarlierSessionIsKept()
        {
            PlayerPrefs.SetInt(KEY_DEFAULT_RUN, 0);
            LocalMovementPreferences.Reload();

            Assert.IsFalse(LocalMovementPreferences.DefaultRun);
        }

        [Test]
        public void ToggleSavesAtOnceAndNotifies()
        {
            int notified = 0;
            void OnChanged() { ++notified; }

            LocalMovementPreferences.Changed += OnChanged;
            try
            {
                LocalMovementPreferences.SetDefaultRun(false);
                Assert.IsFalse(LocalMovementPreferences.DefaultRun);
                Assert.AreEqual(0, PlayerPrefs.GetInt(KEY_DEFAULT_RUN, -1));

                LocalMovementPreferences.SetDefaultRun(true);
                Assert.IsTrue(LocalMovementPreferences.DefaultRun);
                Assert.AreEqual(1, PlayerPrefs.GetInt(KEY_DEFAULT_RUN, -1));
                Assert.AreEqual(2, notified);

                // 다시 읽어도 저장값이 남는다
                LocalMovementPreferences.Reload();
                Assert.IsTrue(LocalMovementPreferences.DefaultRun);
            }
            finally
            {
                LocalMovementPreferences.Changed -= OnChanged;
            }
        }

        // 서버로 가는 SprintHeld는 키 상태가 아니라 실제로 달리려는 의도다
        [TestCase(true,  false, true,  TestName = "RunIntent_DefaultRun_NoShift_Runs")]
        [TestCase(true,  true,  false, TestName = "RunIntent_DefaultRun_Shift_Walks")]
        [TestCase(false, false, false, TestName = "RunIntent_DefaultWalk_NoShift_Walks")]
        [TestCase(false, true,  true,  TestName = "RunIntent_DefaultWalk_Shift_Runs")]
        public void RunIntentFollowsDefaultAndShift(bool defaultRun, bool shiftHeld, bool expectedRun)
        {
            Assert.AreEqual(expectedRun, LocalMovementPreferences.ResolveRunIntent(defaultRun, shiftHeld));
        }

        [Test]
        public void OptionsPageAddsWorkingDefaultRunRow()
        {
            var source = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            Assert.IsNotNull(source, PREFAB_PATH);

            GameObject root = UnityEngine.Object.Instantiate(source);
            root.SetActive(false);
            try
            {
                var binder = root.GetComponentInChildren<SettingsOptionsBinder>(true);
                Assert.IsNotNull(binder, "SettingsOptionsBinder가 프리팹에 없다");

                var minimapRow = (SettingsStepperRow)Get(binder, "_minimapModeRow");
                Assert.IsNotNull(minimapRow, "지도 여는 방식 줄이 끊겼다");
                string minimapLabel = LabelOf(minimapRow);

                Invoke(binder, "Awake");

                var row = (SettingsStepperRow)Get(binder, "_defaultRunRow");
                Assert.IsNotNull(row, "기본 달리기 줄이 만들어지지 않았다");
                Assert.AreEqual(ROW_NAME, row.name);
                Assert.AreSame(minimapRow.transform.parent, row.transform.parent, "게임플레이 목록 밖에 붙었다");
                Assert.AreEqual(minimapRow.transform.GetSiblingIndex() + 1, row.transform.GetSiblingIndex());
                Assert.AreEqual("기본 달리기", LabelOf(row));
                Assert.AreEqual(minimapLabel, LabelOf(minimapRow), "복제하면서 원래 줄 이름을 바꿨다");
                Assert.AreEqual("켜기", ValueOf(row));

                // 선택기 화살표가 내는 신호를 그대로 보낸다. 저장값과 표시가 함께 바뀌어야 한다
                Invoke(row, "StepNext");
                Assert.IsFalse(LocalMovementPreferences.DefaultRun);
                Assert.AreEqual(0, PlayerPrefs.GetInt(KEY_DEFAULT_RUN, -1));
                Assert.AreEqual("끄기", ValueOf(row));

                Invoke(row, "StepPrevious");
                Assert.IsTrue(LocalMovementPreferences.DefaultRun);
                Assert.AreEqual(1, PlayerPrefs.GetInt(KEY_DEFAULT_RUN, -1));
                Assert.AreEqual("켜기", ValueOf(row));

                // 창을 다시 열 때의 갱신도 저장값을 따른다. 전체 Refresh는 해상도, 볼륨까지 건드리므로 게임플레이 줄만 부른다
                LocalMovementPreferences.SetDefaultRun(false);
                Invoke(binder, "RefreshGameplay");
                Assert.AreEqual("끄기", ValueOf(row));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static string LabelOf(SettingsStepperRow row)
        {
            var hint = row.GetComponent<SettingsRowHint>();
            Assert.IsNotNull(hint, row.name + "에 설명 연결이 없다");
            return ((TMP_Text)Get(hint, "_label")).text;
        }

        private static string ValueOf(SettingsStepperRow row)
        {
            return ((TMP_Text)Get(row, "_valueText")).text;
        }

        private static object Get(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, PRIVATE);
            Assert.IsNotNull(field, fieldName);
            return field.GetValue(target);
        }

        private static void Invoke(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, PRIVATE);
            Assert.IsNotNull(method, methodName);
            method.Invoke(target, null);
        }
    }
}
