// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 이 화면에 스폰되어 있는 잡귀의 표현 소스 목록. 표현(머리 위 체력 바 등)이 매 프레임 씬을 훑지 않게 한다
    /// 잡귀(EnemyStatus)가 OnStartClient에서 Register, OnStopClient에서 Unregister한다. 호스트도 클라이언트라 같이 등록된다
    /// 보스는 넣지 않는다 - 보스는 보스 HUD가 따로 보여 준다
    /// 판정과 무관하다. 목록은 서버가 확정해 동기화한 체력 알림(HealthDecreased)을 구독할 대상만 준다
    /// </summary>
    public static class LocalEnemyPresentationRegistry
    {
        /****************************************
        *              Constants
        ****************************************/

        // 한 판에 동시에 살아 있는 잡귀 수보다 넉넉하면 된다. 넘치면 List가 알아서 늘린다
        private const int INITIAL_CAPACITY = 64;

        /****************************************
        *                Fields
        ****************************************/

        private static readonly List<IDamagePresentationSource> G_Sources =
            new List<IDamagePresentationSource>(INITIAL_CAPACITY);

        /****************************************
        *              Events
        ****************************************/

        /// <summary> 잡귀가 이 화면에 나타났다 </summary>
        public static event Action<IDamagePresentationSource> Registered;

        /// <summary> 잡귀가 이 화면에서 사라졌다(디스폰·씬 정리·연결 종료) </summary>
        public static event Action<IDamagePresentationSource> Unregistered;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지금 등록된 잡귀. 구독자가 늦게 생겼을 때 이미 있던 잡귀를 따라잡는 데 쓴다 </summary>
        public static IReadOnlyList<IDamagePresentationSource> Sources => G_Sources;

        /****************************************
        *              Public API
        ****************************************/

        public static void Register(IDamagePresentationSource source)
        {
            if ((source == null) || G_Sources.Contains(source))
            {
                return;
            }

            G_Sources.Add(source);

            // 구독자 하나가 던져도 잡귀의 클라 시작은 이어져야 한다
            ObserverDispatch.Invoke(Registered, source, source as UnityEngine.Object,
                nameof(LocalEnemyPresentationRegistry), nameof(Registered));
        }

        public static void Unregister(IDamagePresentationSource source)
        {
            if ((source == null) || (!G_Sources.Remove(source)))
            {
                return;
            }

            ObserverDispatch.Invoke(Unregistered, source, source as UnityEngine.Object,
                nameof(LocalEnemyPresentationRegistry), nameof(Unregistered));
        }

        /// <summary> 도메인 리로드 OFF 대비 정적 상태 초기화 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            G_Sources.Clear();
            Registered   = null;
            Unregistered = null;
        }
    }
}
