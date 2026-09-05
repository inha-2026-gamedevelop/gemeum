// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 저장된 볼륨을 믹서에 밀어 넣는 일회성 부트.
    /// 씬 로드 직후에 넣으면 믹서가 아직 깨어나지 않아 값이 스냅샷으로 덮인다, 그래서 한 프레임을 기다린다
    /// </summary>
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

        // Start는 첫 프레임에 돈다. 이 시점이면 믹서가 살아 있어 설정한 값이 남는다
        private void Start()
        {
            SoundVolume.Apply();

            // 볼륨을 한 번 밀어 넣으면 할 일이 끝난다, 씬에 남겨 둘 이유가 없다
            Destroy(gameObject);
        }
    }
}
