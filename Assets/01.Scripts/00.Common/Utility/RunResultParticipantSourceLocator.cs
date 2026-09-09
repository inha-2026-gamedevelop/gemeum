// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 서버에 살아 있는 참가자 결과 원본을 중복 없이 보관한다 </summary>
    public static class RunResultParticipantSourceLocator
    {
        private static readonly List<IRunResultParticipantSource> _sources =
            new List<IRunResultParticipantSource>();

        public static void Register(IRunResultParticipantSource source)
        {
            if ((source == null) || (_sources.Contains(source)))
            {
                return;
            }

            _sources.Add(source);
        }

        public static void Unregister(IRunResultParticipantSource source)
        {
            if (source == null)
            {
                return;
            }

            _sources.Remove(source);
        }

        public static void Capture(RunId run, List<RunParticipantStateFact> destination)
        {
            if (destination == null)
            {
                return;
            }

            destination.Clear();
            for (int i = _sources.Count - 1; i >= 0; --i)
            {
                IRunResultParticipantSource source = _sources[i];
                if (IsDestroyedUnityObject(source))
                {
                    _sources.RemoveAt(i);
                    continue;
                }

                if (source.TryCaptureResultState(run, out RunParticipantStateFact state))
                {
                    destination.Add(state);
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            _sources.Clear();
        }

        private static bool IsDestroyedUnityObject(IRunResultParticipantSource source)
        {
            return ((source is Object unityObject) && (unityObject == null));
        }
    }
}
