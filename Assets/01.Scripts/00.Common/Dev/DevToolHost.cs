// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 개발 도구가 씬 배치 없이 스스로 뜨게 하는 공용 호스트.
    ///
    /// 씬이나 프리팹에 도구를 심으면 도구 하나 늘 때마다 머지가 어려운 파일을 고치게 되고,
    /// 시작 씬이 바뀌면 통째로 사라진다(2026-09-08 Boot 시작으로 바꾸며 실제로 겪었다).
    /// 각 도구가 자기 파일에서 RuntimeInitializeOnLoadMethod로 여기에 붙으면
    /// 어느 씬에서 시작하든 같은 도구가 뜨고, 도구 추가는 그 도구의 파일만 고치면 된다.
    ///
    /// 릴리스 빌드에서는 이 클래스 자체가 컴파일되지 않는다
    /// </summary>
    public static class DevToolHost
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD

        /****************************************
        *                Fields
        ****************************************/

        // @ 접두사는 씬 루트 규칙과 같다. 하이라키에서 개발용임이 바로 보인다
        private const string OBJECT_NAME = "@DevTools";

        private static GameObject G_Host;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 도구를 공용 호스트에 붙인다. 이미 붙어 있으면 그것을 그대로 쓴다 </summary>
        public static T Attach<T>() where T : Component
        {
            // 에디트 모드에서는 DontDestroyOnLoad가 예외를 낸다
            if (!Application.isPlaying)
            {
                return null;
            }

            GameObject host = ResolveHost();
            if (host.TryGetComponent(out T existing))
            {
                return existing;
            }

            return host.AddComponent<T>();
        }

        private static GameObject ResolveHost()
        {
            if (G_Host != null)
            {
                return G_Host;
            }

            G_Host = new GameObject(OBJECT_NAME);
            Object.DontDestroyOnLoad(G_Host);

            return G_Host;
        }

#else

        /// <summary> 릴리스에서는 아무것도 만들지 않는다. 호출부를 #if로 감싸지 않아도 되게 남긴다 </summary>
        public static T Attach<T>() where T : Component
        {
            return null;
        }

#endif
    }
}
