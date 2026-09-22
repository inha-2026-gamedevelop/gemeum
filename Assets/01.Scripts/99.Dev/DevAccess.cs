// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Geumeum.Dev
{
    /// <summary>
    /// 개발 도구를 열어 주는 잠금 장치
    ///
    /// 에디터와 개발 빌드에서는 처음부터 열려 있다 - 매 플레이테스트마다 키를 치게 하면
    /// 도구를 안 쓰게 된다. 릴리스 빌드에서는 잠겨 있고, 정해진 키 차례를 맞춰야 열린다
    ///
    /// 여는 것을 "치트 모드"라고 부르지만 새 치트를 만들지는 않는다 -
    /// 이미 있는 개발 도구(브라우저 패널, 화면 로그, 기즈모)를 릴리스에서도 쓰게 할 뿐이다
    ///
    /// GEUMEUM_DEVTOOLS를 빼고 빌드하면 이 클래스와 도구 전체가 컴파일되지 않는다
    /// 진짜 출시 빌드는 그렇게 낸다
    /// </summary>
    public static class DevAccess
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS

        /****************************************
        *              Constants
        ****************************************/

        // 게임 조작과 겹치지 않는 이동키 무리다. 순서대로 눌러야 하고, 하나라도 틀리면 처음으로 돌아간다
        // SteamLobbyHUD가 Home / PageDown / End를 이미 쓰지만, 잠겨 있는 동안에는 그 도구 자체가
        // 떠 있지 않으므로 해제 도중에는 겹치지 않는다
        private static readonly KeyCode[] G_UnlockSequence =
        {
            KeyCode.Home,
            KeyCode.PageUp,
            KeyCode.PageDown,
            KeyCode.End,
        };

        // 앞 키를 누른 지 이 시간이 지나면 이어지는 입력으로 보지 않는다
        // 게임 도중 우연히 눌린 키가 몇 분 뒤 입력과 이어져 열리는 것을 막는다
        private const float STEP_TIMEOUT = 3f;

        private const string OBJECT_NAME = "@DevAccess";

        /****************************************
        *                Fields
        ****************************************/

        // 잠금이 풀릴 때까지 기다리는 일들. 도구마다 자기 자리에서 #if를 쓰지 않게 하려고 여기서 모아 둔다
        private static readonly List<Action> _pending = new List<Action>(16);

        private static bool _isUnlocked = false;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 개발 도구를 써도 되는 상태인지 </summary>
        public static bool IsUnlocked => _isUnlocked;

        /// <summary> 잠금이 풀린 순간 한 번 불린다 </summary>
        public static event Action Unlocked;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 잠금이 풀렸을 때 할 일을 건다. 이미 풀려 있으면 그 자리에서 실행한다
        /// 도구가 스스로 뜨는 자리(RuntimeInitializeOnLoadMethod)를 이걸로 감싸면
        /// 잠긴 동안에는 오브젝트도 스레드도 만들어지지 않는다
        /// </summary>
        public static void RunWhenUnlocked(Action action)
        {
            if (action == null)
            {
                return;
            }

            if (_isUnlocked)
            {
                action();
                return;
            }

            _pending.Add(action);
        }

        /// <summary>
        /// 씬이나 프리팹에 배치된 개발 도구를 잠긴 동안 꺼 둔다. 열리면 다시 켠다
        /// 꺼 두면 Start도 미뤄지므로, 패널 등록 같은 초기화도 열린 뒤에야 일어난다
        /// </summary>
        public static void GateComponent(MonoBehaviour component)
        {
            if ((component == null) || (_isUnlocked))
            {
                return;
            }

            component.enabled = false;

            RunWhenUnlocked(() =>
            {
                // 잠금이 풀리기 전에 파괴됐을 수 있다
                if (component != null)
                {
                    component.enabled = true;
                }
            });
        }

        /// <summary> 잠금을 푼다. 키 차례 말고 다른 경로로도 열 수 있게 공개한다 </summary>
        public static void Unlock()
        {
            if (_isUnlocked)
            {
                return;
            }

            _isUnlocked = true;

            Debug.Log("[DevAccess] 치트 모드를 켰다. 개발 도구가 열린다");

            // 도구 하나가 터져도 나머지는 떠야 한다. 개발 도구가 게임을 멈추면 안 된다
            for (int i = 0; i < _pending.Count; ++i)
            {
                try
                {
                    _pending[i]();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            _pending.Clear();

            Unlocked?.Invoke();
        }

        /****************************************
        *                Preload
        ****************************************/

        /// <summary> 도메인 리로드를 끈 환경에서 이전 플레이의 상태가 남지 않게 한다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _pending.Clear();
            Unlocked = null;

            // 에디터와 개발 빌드는 잠글 이유가 없다. 릴리스에서만 키 차례를 요구한다
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _isUnlocked = true;
#else
            _isUnlocked = false;
#endif
        }

        /// <summary> 잠겨 있을 때만 키를 지켜본다. 열리면 감시기는 스스로 사라진다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Preload()
        {
            if ((!Application.isPlaying) || (_isUnlocked))
            {
                return;
            }

            GameObject host = new GameObject(OBJECT_NAME);
            host.AddComponent<DevAccessWatcher>();
            UnityEngine.Object.DontDestroyOnLoad(host);
        }

        /****************************************
        *                Types
        ****************************************/

        /// <summary> 해제 키 차례만 본다. 열리는 즉시 자기 GameObject를 지운다 </summary>
        private sealed class DevAccessWatcher : MonoBehaviour
        {
            private int   _step         = 0;
            private float _stepDeadline = 0f;

            private void Update()
            {
                if (_isUnlocked)
                {
                    Destroy(gameObject);
                    return;
                }

                if ((_step > 0) && (Time.unscaledTime > _stepDeadline))
                {
                    _step = 0;
                }

                // 매 프레임 KeyCode 네 개를 다 보지 않게 한다. 잠긴 릴리스에서 늘 도는 자리다
                if (!Input.anyKeyDown)
                {
                    return;
                }

                if (!Input.GetKeyDown(G_UnlockSequence[_step]))
                {
                    // 차례의 첫 키를 다시 눌렀으면 거기서 새로 시작한다
                    // 그렇게 하지 않으면 Home을 두 번 친 순간 영영 안 열린다
                    _step         = (Input.GetKeyDown(G_UnlockSequence[0])) ? 1 : 0;
                    _stepDeadline = Time.unscaledTime + STEP_TIMEOUT;
                    return;
                }

                ++_step;
                _stepDeadline = Time.unscaledTime + STEP_TIMEOUT;

                if (_step < G_UnlockSequence.Length)
                {
                    return;
                }

                Unlock();
                Destroy(gameObject);
            }
        }

#else

        /// <summary> 도구를 빼고 빌드했을 때. 호출부를 #if로 감싸지 않아도 되게 남긴다 </summary>
        public static bool IsUnlocked => false;

        /// <summary> 도구가 없으므로 아무것도 걸리지 않는다 </summary>
        public static void RunWhenUnlocked(Action action)
        {
        }

        /// <summary> 도구가 없으므로 꺼 둘 것도 없다 </summary>
        public static void GateComponent(MonoBehaviour component)
        {
        }

        /// <summary> 도구가 없으므로 열 것도 없다 </summary>
        public static void Unlock()
        {
        }

#endif
    }
}
