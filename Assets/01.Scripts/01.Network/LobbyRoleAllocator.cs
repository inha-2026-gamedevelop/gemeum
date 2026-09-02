// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary>
    /// 무기 셔플과 외형 충돌 해결만 담는 순수 계산기. Mirror와 UnityEngine에 의존하지 않아
    /// 접속 없이도 결과를 검증할 수 있다. 실제 적용과 슬롯 갱신은 LobbyController가 한다
    /// </summary>
    public static class LobbyRoleAllocator
    {
        /// <summary>
        /// 한 사람이 이미 가진 것. 로비에서 고른 외형과, 직전 런에서 쓰다가 이어받은 무기다.
        /// 아무것도 정해지지 않았으면 두 Has 플래그가 모두 false이고 배분기가 채운다
        /// </summary>
        public readonly struct RoleRequest
        {
            public readonly bool           HasWeapon;
            public readonly WeaponKind     Weapon;
            public readonly bool           HasAppearance;
            public readonly AppearanceKind Appearance;

            public RoleRequest(bool hasWeapon, WeaponKind weapon, bool hasAppearance, AppearanceKind appearance)
            {
                HasWeapon     = hasWeapon;
                Weapon        = weapon;
                HasAppearance = hasAppearance;
                Appearance    = appearance;
            }
        }

        /// <summary> 배분에 실패한 이유. 호출자가 로그 문구를 정한다 </summary>
        public enum FailureKind
        {
            None              = 0,
            NotEnoughWeapons  = 1, // 무기 종류가 인원보다 적다
            NotEnoughLooks    = 2, // 외형 종류가 인원보다 적다
            InvalidAppearance = 3, // 고른 외형이 정의되지 않았거나 이미 다른 사람이 가져갔다
            NoFreeAppearance  = 4, // 고르지 않은 사람에게 줄 외형이 남지 않았다
            InvalidWeapon     = 5, // 이어받은 무기가 정의되지 않았거나 이미 다른 사람이 가져갔다
            NoFreeWeapon      = 6, // 무기가 없는 사람에게 줄 무기가 남지 않았다
        }

        /// <summary>
        /// 인원수만큼 무기와 외형을 겹치지 않게 정한다.
        /// 실패하면 out 배열은 null이고 failedIndex는 문제가 된 요청의 인덱스다(해당 없으면 -1)
        /// </summary>
        public static bool TryAllocate(
            IReadOnlyList<RoleRequest> requests,
            Random                     serverRandom,
            bool                       forceSword,
            out WeaponKind[]           weapons,
            out AppearanceKind[]       appearances,
            out FailureKind            failure,
            out int                    failedIndex)
        {
            weapons     = null;
            appearances = null;
            failure     = FailureKind.None;
            failedIndex = -1;

            if ((requests == null) || (serverRandom == null))
            {
                failure = FailureKind.NotEnoughWeapons;
                return false;
            }

            WeaponKind[]     weaponPool     = (WeaponKind[])     Enum.GetValues(typeof(WeaponKind));
            AppearanceKind[] appearancePool = (AppearanceKind[]) Enum.GetValues(typeof(AppearanceKind));

            if (weaponPool.Length < requests.Count)
            {
                failure = FailureKind.NotEnoughWeapons;
                return false;
            }

            if (appearancePool.Length < requests.Count)
            {
                failure = FailureKind.NotEnoughLooks;
                return false;
            }

            if (!TryResolveAppearances(requests, appearancePool, out appearances, out failure, out failedIndex))
            {
                appearances = null;
                return false;
            }

            if (!TryResolveWeapons(requests, weaponPool, serverRandom, forceSword, out weapons, out failure, out failedIndex))
            {
                weapons     = null;
                appearances = null;
                return false;
            }

            return true;
        }

        // 이미 가진 무기는 그대로 두고, 없는 사람에게만 남은 무기를 무작위로 준다.
        // 재접속 복구에서 직전 런의 직업이 그대로 이어지도록 하려는 것이다
        private static bool TryResolveWeapons(
            IReadOnlyList<RoleRequest> requests,
            WeaponKind[]               pool,
            Random                     serverRandom,
            bool                       forceSword,
            out WeaponKind[]           resolved,
            out FailureKind            failure,
            out int                    failedIndex)
        {
            resolved    = new WeaponKind[requests.Count];
            failure     = FailureKind.None;
            failedIndex = -1;

            // Dev 설정은 이어받은 무기보다 우선한다, 검 하나로 고정해 두고 보는 용도다
            if (forceSword)
            {
                for (int i = 0; i < resolved.Length; ++i)
                {
                    resolved[i] = WeaponKind.Sword;
                }

                return true;
            }

            bool[] isTaken = new bool[pool.Length];

            for (int i = 0; i < requests.Count; ++i)
            {
                if (!requests[i].HasWeapon)
                {
                    continue;
                }

                int index = Array.IndexOf(pool, requests[i].Weapon);

                if ((index < 0) || (isTaken[index]))
                {
                    failure     = FailureKind.InvalidWeapon;
                    failedIndex = i;
                    return false;
                }

                isTaken[index] = true;
                resolved[i]    = requests[i].Weapon;
            }

            List<WeaponKind> free = new List<WeaponKind>(pool.Length);
            for (int i = 0; i < pool.Length; ++i)
            {
                if (!isTaken[i])
                {
                    free.Add(pool[i]);
                }
            }

            // 접속 순서나 이름이 아니라 서버 난수로 순열을 만든다
            for (int i = free.Count - 1; i > 0; --i)
            {
                int swapIndex = serverRandom.Next(i + 1);

                WeaponKind temp = free[i];
                free[i]         = free[swapIndex];
                free[swapIndex] = temp;
            }

            int nextFree = 0;
            for (int i = 0; i < requests.Count; ++i)
            {
                if (requests[i].HasWeapon)
                {
                    continue;
                }

                if (nextFree >= free.Count)
                {
                    failure     = FailureKind.NoFreeWeapon;
                    failedIndex = i;
                    return false;
                }

                resolved[i] = free[nextFree];
                ++nextFree;
            }

            return true;
        }

        // 로비에서 고른 외형은 그대로 두고, 고르지 않은 사람에게만 남은 외형을 준다.
        // 무기와 달리 섞지 않는다 - 고른 사람의 선택을 뒤집지 않기 위해서다
        private static bool TryResolveAppearances(
            IReadOnlyList<RoleRequest> requests,
            AppearanceKind[]           pool,
            out AppearanceKind[]       resolved,
            out FailureKind            failure,
            out int                    failedIndex)
        {
            resolved    = new AppearanceKind[requests.Count];
            failure     = FailureKind.None;
            failedIndex = -1;

            bool[] isTaken = new bool[pool.Length];

            for (int i = 0; i < requests.Count; ++i)
            {
                if (!requests[i].HasAppearance)
                {
                    continue;
                }

                int index = Array.IndexOf(pool, requests[i].Appearance);

                // 선택 시점에 중복과 미정의 값을 막지만, 여기서 뚫리면 두 사람이 같은 모습이 된다
                if ((index < 0) || (isTaken[index]))
                {
                    failure     = FailureKind.InvalidAppearance;
                    failedIndex = i;
                    return false;
                }

                isTaken[index] = true;
                resolved[i]    = requests[i].Appearance;
            }

            for (int i = 0; i < requests.Count; ++i)
            {
                if (requests[i].HasAppearance)
                {
                    continue;
                }

                int freeIndex = Array.IndexOf(isTaken, false);
                if (freeIndex < 0)
                {
                    failure     = FailureKind.NoFreeAppearance;
                    failedIndex = i;
                    return false;
                }

                isTaken[freeIndex] = true;
                resolved[i]        = pool[freeIndex];
            }

            return true;
        }
    }
}
