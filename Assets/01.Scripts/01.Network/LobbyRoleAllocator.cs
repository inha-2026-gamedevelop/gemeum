// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary> 무기 셔플과 캐릭터 충돌 해결만 담는 순수 계산기 </summary>
    public static class LobbyRoleAllocator
    {
        /// <summary> 한 사람이 이미 가진 캐릭터와 무기 </summary>
        public readonly struct RoleRequest
        {
            public readonly bool           HasWeapon;
            public readonly WeaponKind     Weapon;
            public readonly bool           HasCharacter;
            public readonly CharacterKind  Character;

            public RoleRequest(bool hasWeapon, WeaponKind weapon, bool hasCharacter, CharacterKind character)
            {
                HasWeapon     = hasWeapon;
                Weapon        = weapon;
                HasCharacter  = hasCharacter;
                Character     = character;
            }
        }

        /// <summary> 배분에 실패한 이유. 호출자가 로그 문구를 정한다 </summary>
        public enum FailureKind
        {
            None                = 0,
            NotEnoughWeapons    = 1, // 무기 종류가 인원보다 적다
            NotEnoughCharacters = 2, // 캐릭터 종류가 인원보다 적다
            InvalidCharacter    = 3, // 고른 캐릭터가 정의되지 않았거나 이미 다른 사람이 가져갔다
            NoFreeCharacter     = 4, // 고르지 않은 사람에게 줄 캐릭터가 남지 않았다
            InvalidWeapon       = 5, // 이어받은 무기가 정의되지 않았거나 이미 다른 사람이 가져갔다
            NoFreeWeapon        = 6, // 무기가 없는 사람에게 줄 무기가 남지 않았다
        }

        /// <summary> 인원수만큼 무기와 캐릭터를 겹치지 않게 정한다 </summary>
        public static bool TryAllocate(
            IReadOnlyList<RoleRequest> requests,
            Random                     serverRandom,
            bool                       forceSword,
            out WeaponKind[]           weapons,
            out CharacterKind[]       characters,
            out FailureKind            failure,
            out int                    failedIndex)
        {
            weapons     = null;
            characters = null;
            failure     = FailureKind.None;
            failedIndex = -1;

            if ((requests == null) || (serverRandom == null))
            {
                failure = FailureKind.NotEnoughWeapons;
                return false;
            }

            WeaponKind[]     weaponPool     = (WeaponKind[])     Enum.GetValues(typeof(WeaponKind));
            CharacterKind[] characterPool = (CharacterKind[]) Enum.GetValues(typeof(CharacterKind));

            if (weaponPool.Length < requests.Count)
            {
                failure = FailureKind.NotEnoughWeapons;
                return false;
            }

            if (characterPool.Length < requests.Count)
            {
                failure = FailureKind.NotEnoughCharacters;
                return false;
            }

            if (!TryResolveCharacters(requests, characterPool, out characters, out failure, out failedIndex))
            {
                characters = null;
                return false;
            }

            weapons = ResolveWeapons(characters, forceSword);

            return true;
        }

        private static WeaponKind[] ResolveWeapons(CharacterKind[] characters, bool forceSword)
        {
            var resolved = new WeaponKind[characters.Length];

            for (int i = 0; i < resolved.Length; ++i)
            {
                resolved[i] = forceSword ? WeaponKind.Sword : ToWeapon(characters[i]);
            }

            return resolved;
        }

        /// <summary> 캐릭터에 딸린 무기 </summary>
        public static WeaponKind ToWeapon(CharacterKind character)
        {
            switch (character)
            {
                case CharacterKind.Chasa:   return WeaponKind.Sword;
                case CharacterKind.Singung: return WeaponKind.Bow;
                case CharacterKind.Munyeo:  return WeaponKind.Bell;
                default:                    return WeaponKind.Sword;
            }
        }

        private static bool TryResolveCharacters(
            IReadOnlyList<RoleRequest> requests,
            CharacterKind[]           pool,
            out CharacterKind[]       resolved,
            out FailureKind            failure,
            out int                    failedIndex)
        {
            resolved    = new CharacterKind[requests.Count];
            failure     = FailureKind.None;
            failedIndex = -1;

            bool[] isTaken = new bool[pool.Length];

            for (int i = 0; i < requests.Count; ++i)
            {
                if (!requests[i].HasCharacter)
                {
                    continue;
                }

                int index = Array.IndexOf(pool, requests[i].Character);

                if ((index < 0) || (isTaken[index]))
                {
                    failure     = FailureKind.InvalidCharacter;
                    failedIndex = i;
                    return false;
                }

                isTaken[index] = true;
                resolved[i]    = requests[i].Character;
            }

            for (int i = 0; i < requests.Count; ++i)
            {
                if (requests[i].HasCharacter)
                {
                    continue;
                }

                int freeIndex = Array.IndexOf(isTaken, false);
                if (freeIndex < 0)
                {
                    failure     = FailureKind.NoFreeCharacter;
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
