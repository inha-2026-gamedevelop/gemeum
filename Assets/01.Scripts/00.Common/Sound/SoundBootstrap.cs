// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 저장된 볼륨을 믹서에 밀어 넣는 일회성 부트. 첫 프레임까지 기다렸다가 적용한다 </summary>
    public class SoundBootstrap : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string OBJECT_NAME = "@SoundBootstrap";

        /****************************************
        *              Unity Event
        ****************************************/

        // 씬에 배치하지 않아도 게임 시작 때 한 번 돈다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            GameObject host = new GameObject(OBJECT_NAME);
            host.hideFlags  = HideFlags.HideAndDontSave;

            DontDestroyOnLoad(host);
            host.AddComponent<SoundBootstrap>();
        }

        private void Start()
        {
            SoundVolume.Apply();
            Destroy(gameObject);
        }
    }
}
