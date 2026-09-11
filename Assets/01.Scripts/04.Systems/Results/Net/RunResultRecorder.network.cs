// Unity
using UnityEngine;

namespace Geumeum.Systems
{
    public sealed partial class RunResultRecorder
    {
        /****************************************
        *           Client Callbacks
        ****************************************/

        public override void OnStartClient()
        {
            if (_runManager == null)
            {
                Debug.LogError("[RunResultRecorder] RunManager가 없어 결과를 기록할 수 없다", this);
                return;
            }

            _runManager.ResultChanged += HandleResultChanged;
            HandleResultChanged(_runManager.Result);
        }

        public override void OnStopClient()
        {
            if (_runManager != null)
            {
                _runManager.ResultChanged -= HandleResultChanged;
            }
        }
    }
}
