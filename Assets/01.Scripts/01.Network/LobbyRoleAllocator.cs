// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary> 선호를 받아 캐릭터와 무기를 겹치지 않게 정하는 순수 계산기 </summary>
    public static class LobbyRoleAllocator
    {
        /// <summary> 한 사람의 선호와 이미 확정된 역할 </summary>
        public readonly struct RoleRequest
        {
            /// <summary> 정렬 기준. 접속 순서가 결과를 바꾸지 않게 한다 </summary>
            public readonly ulong          ParticipantKey;

            public readonly bool           HasWeapon;
            public readonly WeaponKind     Weapon;
            public readonly bool           HasCharacter;
            public readonly CharacterKind  Character;
            public readonly bool           HasPreference;
            public readonly CharacterKind  PreferredCharacter;

            public RoleRequest(
                ulong          participantKey,
                bool           hasWeapon,
                WeaponKind     weapon,
                bool           hasCharacter,
                CharacterKind  character,
                bool           hasPreference,
                CharacterKind  preferredCharacter)
            {
                ParticipantKey     = participantKey;
                HasWeapon          = hasWeapon;
                Weapon             = weapon;
                HasCharacter       = hasCharacter;
                Character          = character;
                HasPreference      = hasPreference;
                PreferredCharacter = preferredCharacter;
            }
        }

        /// <summary> 배분에 실패한 이유. 호출자가 로그 문구를 정한다 </summary>
        public enum FailureKind
        {
            None                = 0,
            NotEnoughWeapons    = 1, // 무기 종류가 인원보다 적다
            NotEnoughCharacters = 2, // 캐릭터 종류가 인원보다 적다
            InvalidCharacter    = 3, // 이어받은 캐릭터가 정의되지 않았거나 이미 다른 사람이 가져갔다
            NoFreeCharacter     = 4, // 배정할 캐릭터가 남지 않았다
            InvalidWeapon       = 5, // 이어받은 무기가 정의되지 않았거나 이미 다른 사람이 가져갔다
            NoFreeWeapon        = 6, // 무기가 없는 사람에게 줄 무기가 남지 않았다
        }

        /// <summary> 인원수만큼 무기와 캐릭터를 겹치지 않게 정한다 </summary>
        public static bool TryAllocate(
            IReadOnlyList<RoleRequest> requests,
            Random                     serverRandom,
            bool                       forceSword,
            out WeaponKind[]           weapons,
            out CharacterKind[]        characters,
            out FailureKind            failure,
            out int                    failedIndex)
        {
            weapons     = null;
            characters  = null;
            failure     = FailureKind.None;
            failedIndex = -1;

            if ((requests == null) || (serverRandom == null))
            {
                failure = FailureKind.NotEnoughWeapons;
                return false;
            }

            WeaponKind[]    weaponPool    = (WeaponKind[])    Enum.GetValues(typeof(WeaponKind));
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

            if (!TryResolveCharacters(requests, characterPool, serverRandom, out characters, out failure, out failedIndex))
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

        /// <summary> 캐릭터에 딸린 무기. 시작 배분에만 쓰고 런 중 교체는 무기를 직접 바꾼다 </summary>
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
            CharacterKind[]            pool,
            Random                     serverRandom,
            out CharacterKind[]        resolved,
            out FailureKind            failure,
            out int                    failedIndex)
        {
            resolved    = new CharacterKind[requests.Count];
            failure     = FailureKind.None;
            failedIndex = -1;

            bool[] isTaken   = new bool[pool.Length];
            bool[] isSettled = new bool[requests.Count];

            // 이어받은 역할을 먼저 잠근다. 재접속은 추첨을 다시 거치지 않는다
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
                isSettled[i]   = true;
                resolved[i]    = requests[i].Character;
            }

            int[] order = BuildParticipantOrder(requests);

            AssignPreferred(requests, pool, order, serverRandom, isTaken, isSettled, resolved);

            return TryAssignRemainder(pool, order, serverRandom, isTaken, isSettled, resolved,
                out failure, out failedIndex);
        }

        /// <summary> 같은 사람들이 모이면 접속 순서와 무관하게 같은 입력이 되도록 참가자 키로 줄을 세운다 </summary>
        private static int[] BuildParticipantOrder(IReadOnlyList<RoleRequest> requests)
        {
            int[] order = new int[requests.Count];

            for (int i = 0; i < order.Length; ++i)
            {
                order[i] = i;
            }

            // 신원이 없어 키가 0으로 겹치면 원래 자리로 갈라 정렬이 흔들리지 않게 한다
            Array.Sort(order, (left, right) =>
            {
                int byKey = requests[left].ParticipantKey.CompareTo(requests[right].ParticipantKey);
                return (byKey != 0) ? byKey : left.CompareTo(right);
            });

            return order;
        }

        /// <summary> 캐릭터마다 원한 사람 중 하나를 뽑는다. 혼자 원했으면 그대로 받는다 </summary>
        private static void AssignPreferred(
            IReadOnlyList<RoleRequest> requests,
            CharacterKind[]            pool,
            int[]                      order,
            Random                     serverRandom,
            bool[]                     isTaken,
            bool[]                     isSettled,
            CharacterKind[]            resolved)
        {
            var candidates = new List<int>(order.Length);

            for (int p = 0; p < pool.Length; ++p)
            {
                if (isTaken[p])
                {
                    continue;
                }

                candidates.Clear();

                for (int k = 0; k < order.Length; ++k)
                {
                    int i = order[k];

                    if ((isSettled[i]) || (!requests[i].HasPreference))
                    {
                        continue;
                    }

                    if (requests[i].PreferredCharacter == pool[p])
                    {
                        candidates.Add(i);
                    }
                }

                if (candidates.Count == 0)
                {
                    continue;
                }

                int winner = candidates[serverRandom.Next(candidates.Count)];

                isTaken[p]        = true;
                isSettled[winner] = true;
                resolved[winner]  = pool[p];
            }
        }

        /// <summary> 밀린 사람과 남은 캐릭터를 각각 섞어 일대일로 짝지운다 </summary>
        private static bool TryAssignRemainder(
            CharacterKind[]     pool,
            int[]               order,
            Random              serverRandom,
            bool[]              isTaken,
            bool[]              isSettled,
            CharacterKind[]     resolved,
            out FailureKind     failure,
            out int             failedIndex)
        {
            failure     = FailureKind.None;
            failedIndex = -1;

            var waiting = new List<int>(order.Length);
            for (int k = 0; k < order.Length; ++k)
            {
                if (!isSettled[order[k]])
                {
                    waiting.Add(order[k]);
                }
            }

            if (waiting.Count == 0)
            {
                return true;
            }

            var free = new List<CharacterKind>(pool.Length);
            for (int p = 0; p < pool.Length; ++p)
            {
                if (!isTaken[p])
                {
                    free.Add(pool[p]);
                }
            }

            if (free.Count < waiting.Count)
            {
                failure     = FailureKind.NoFreeCharacter;
                failedIndex = waiting[free.Count];
                return false;
            }

            Shuffle(waiting, serverRandom);
            Shuffle(free, serverRandom);

            for (int i = 0; i < waiting.Count; ++i)
            {
                isSettled[waiting[i]] = true;
                resolved[waiting[i]]  = free[i];
            }

            return true;
        }

        private static void Shuffle<T>(IList<T> items, Random serverRandom)
        {
            for (int i = items.Count - 1; i > 0; --i)
            {
                int j = serverRandom.Next(i + 1);

                T swap   = items[i];
                items[i] = items[j];
                items[j] = swap;
            }
        }
    }
}
