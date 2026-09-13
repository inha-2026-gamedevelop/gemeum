// Unity
using UnityEngine;

// 서드파티
using TMPro;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.UI
{
    /// <summary>
    /// 우상단 시간 슬롯의 숫자를 적는다.
    ///
    /// 서버가 보낸 시계 하나에서 각 클라이언트가 계산한다 - 매 초 값을 받지도, 로컬로 세지도 않는다.
    /// 그래서 런 도중 들어온 사람도 같은 숫자를 본다.
    ///
    /// 지금은 경과 시간만 적는다. 남은 시간과 단계별 색 전환은 결계 축소 시각과 보스 종료 시각이
    /// 창구에 올라온 뒤에 붙인다
    /// </summary>
    public class HudTimerController : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int SECONDS_PER_MINUTE = 60;

        // 아직 한 번도 적지 않았다는 표시. 어떤 경과 시간과도 같지 않아야 한다
        private const int NOT_SHOWN = -1;

        [Header("표시")]
        [SerializeField] private TMP_Text _label = null;

        // 초가 그대로인데 다시 적으면 TMP가 메시를 매 프레임 다시 만든다
        private int _shownSeconds = NOT_SHOWN;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            _shownSeconds = NOT_SHOWN; // 다시 켜졌을 때 이전 런의 숫자가 남지 않게 한 번은 적는다
        }

        private void Update()
        {
            if (_label == null)
            {
                return;
            }

            int seconds = Mathf.Max(0, Mathf.FloorToInt((float) RunTimeLocator.Current.GameplayElapsedSeconds));
            if (seconds == _shownSeconds)
            {
                return;
            }

            _shownSeconds = seconds;

            _label.text = $"{(seconds / SECONDS_PER_MINUTE):00}:{(seconds % SECONDS_PER_MINUTE):00}";
        }
    }
}
