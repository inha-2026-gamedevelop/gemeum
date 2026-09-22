// Unity
using UnityEngine;

// Third party
using Mirror;

// Project
using Geumeum.Common;
using Geumeum.Network;

namespace Geumeum.Art
{
    /// <summary>Plays the boss gate cue once while the replicated entrance route enters</summary>
    [DisallowMultipleComponent]
    public sealed class RunEntranceSoundView : MonoBehaviour
    {
        private RunManager _source;
        private BossEntranceState _previousState;
        private bool _hasBaseline;

        private void Update()
        {
            RunManager current = RunManager.LocalInstance;
            if (current != _source)
            {
                _source = current;
                _hasBaseline = false;
            }

            if (_source == null)
            {
                return;
            }

            BossEntranceState currentState = _source.EntranceState;
            if (RunEntranceSoundRules.ShouldPlayGate(
                    _hasBaseline, _previousState, currentState,
                    NetworkClient.active))
            {
                SfxPlayer.Play(SoundKey.BossGateOpen, false);
            }

            _previousState = currentState;
            _hasBaseline = true;
        }
    }

    public static class RunEntranceSoundRules
    {
        public static bool ShouldPlayGate(bool hasBaseline,
            BossEntranceState previousState, BossEntranceState currentState,
            bool clientActive)
        {
            return clientActive &&
                (currentState == BossEntranceState.Entering) &&
                ((!hasBaseline) || (previousState != BossEntranceState.Entering));
        }
    }
}
