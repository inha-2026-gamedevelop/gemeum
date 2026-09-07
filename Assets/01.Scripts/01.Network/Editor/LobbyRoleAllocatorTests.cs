using System;
using System.Collections.Generic;

using NUnit.Framework;

using Geumeum.Common;
using Geumeum.Network;

/// <summary> 로비 선호 배분의 규칙. 3인 27조합과 고정 seed로 검사한다 </summary>
public class LobbyRoleAllocatorTests
{
    private const int PARTY_SIZE   = 3;
    private const int FIXED_SEED   = 20260907;
    private const int SEED_SAMPLES = 40;      // 추첨이 걸린 항목은 여러 seed로 반복해 우연한 통과를 걸러낸다

    private static LobbyRoleAllocator.RoleRequest Preferring(ulong key, CharacterKind character)
    {
        return new LobbyRoleAllocator.RoleRequest(key, false, default, false, default, true, character);
    }

    private static LobbyRoleAllocator.RoleRequest NoPreference(ulong key)
    {
        return new LobbyRoleAllocator.RoleRequest(key, false, default, false, default, false, default);
    }

    /// <summary> 이미 역할을 받고 이탈했다가 돌아온 사람 </summary>
    private static LobbyRoleAllocator.RoleRequest Holding(ulong key, CharacterKind character)
    {
        return new LobbyRoleAllocator.RoleRequest(
            key, true, LobbyRoleAllocator.ToWeapon(character), true, character, false, default);
    }

    private static CharacterKind[] Allocate(IReadOnlyList<LobbyRoleAllocator.RoleRequest> requests, int seed)
    {
        bool ok = LobbyRoleAllocator.TryAllocate(
            requests,
            new Random(seed),
            false,
            out WeaponKind[] weapons,
            out CharacterKind[] characters,
            out LobbyRoleAllocator.FailureKind failure,
            out int failedIndex);

        Assert.IsTrue(ok, $"배분에 실패했다: {failure}, index={failedIndex}");
        Assert.AreEqual(requests.Count, characters.Length);
        Assert.AreEqual(requests.Count, weapons.Length);

        // 무기는 캐릭터에서 파생되므로 함께 검사한다
        for (int i = 0; i < characters.Length; ++i)
        {
            Assert.AreEqual(LobbyRoleAllocator.ToWeapon(characters[i]), weapons[i]);
        }

        return characters;
    }

    private static void AssertOneOfEach(CharacterKind[] characters)
    {
        var seen = new HashSet<CharacterKind>(characters);
        Assert.AreEqual(PARTY_SIZE, seen.Count, "세 사람이 서로 다른 캐릭터를 받아야 한다");
    }

    /// <summary> 선호가 어떻게 겹치든 셋은 항상 서로 다른 캐릭터를 받는다 </summary>
    [Test]
    public void EveryPreferenceCombinationYieldsOneOfEach()
    {
        var pool = (CharacterKind[]) Enum.GetValues(typeof(CharacterKind));

        for (int a = 0; a < pool.Length; ++a)
        {
            for (int b = 0; b < pool.Length; ++b)
            {
                for (int c = 0; c < pool.Length; ++c)
                {
                    var requests = new[]
                    {
                        Preferring(1UL, pool[a]),
                        Preferring(2UL, pool[b]),
                        Preferring(3UL, pool[c]),
                    };

                    for (int seed = 0; seed < SEED_SAMPLES; ++seed)
                    {
                        AssertOneOfEach(Allocate(requests, seed));
                    }
                }
            }
        }
    }

    /// <summary> 혼자 원한 사람은 추첨을 거치지 않고 그대로 받는다 </summary>
    [Test]
    public void SolePreferenceIsAlwaysHonored()
    {
        var requests = new[]
        {
            Preferring(1UL, CharacterKind.Chasa),
            Preferring(2UL, CharacterKind.Chasa),
            Preferring(3UL, CharacterKind.Munyeo),   // 무녀를 원한 사람은 이 사람뿐이다
        };

        for (int seed = 0; seed < SEED_SAMPLES; ++seed)
        {
            CharacterKind[] result = Allocate(requests, seed);

            Assert.AreEqual(CharacterKind.Munyeo, result[2], $"seed={seed}에서 단독 선호가 지켜지지 않았다");
            AssertOneOfEach(result);
        }
    }

    /// <summary> 셋이 같은 캐릭터를 원하면 한 명만 받고 나머지는 남은 것을 나눠 갖는다 </summary>
    [Test]
    public void AllWantingSameGivesItToExactlyOne()
    {
        var requests = new[]
        {
            Preferring(1UL, CharacterKind.Chasa),
            Preferring(2UL, CharacterKind.Chasa),
            Preferring(3UL, CharacterKind.Chasa),
        };

        for (int seed = 0; seed < SEED_SAMPLES; ++seed)
        {
            CharacterKind[] result = Allocate(requests, seed);

            int winners = 0;
            for (int i = 0; i < result.Length; ++i)
            {
                if (result[i] == CharacterKind.Chasa)
                {
                    ++winners;
                }
            }

            Assert.AreEqual(1, winners, $"seed={seed}에서 차사를 받은 사람이 하나가 아니다");
            AssertOneOfEach(result);
        }
    }

    /// <summary> 추첨이 실제로 돌아 같은 입력에서도 seed가 다르면 승자가 갈린다 </summary>
    [Test]
    public void LotteryActuallyVariesAcrossSeeds()
    {
        var requests = new[]
        {
            Preferring(1UL, CharacterKind.Chasa),
            Preferring(2UL, CharacterKind.Chasa),
            Preferring(3UL, CharacterKind.Chasa),
        };

        var winners = new HashSet<int>();

        for (int seed = 0; seed < SEED_SAMPLES; ++seed)
        {
            CharacterKind[] result = Allocate(requests, seed);

            for (int i = 0; i < result.Length; ++i)
            {
                if (result[i] == CharacterKind.Chasa)
                {
                    winners.Add(i);
                }
            }
        }

        Assert.Greater(winners.Count, 1, "seed를 바꿔도 늘 같은 사람이 이기면 추첨이 돌지 않는 것이다");
    }

    /// <summary> 같은 요청을 다시 보내도 같은 결과다. 요청마다 다시 뽑지 않는다 </summary>
    [Test]
    public void SameSeedAndInputGivesSameResult()
    {
        var requests = new[]
        {
            Preferring(1UL, CharacterKind.Chasa),
            Preferring(2UL, CharacterKind.Chasa),
            Preferring(3UL, CharacterKind.Singung),
        };

        CharacterKind[] first = Allocate(requests, FIXED_SEED);

        for (int repeat = 0; repeat < 5; ++repeat)
        {
            CollectionAssert.AreEqual(first, Allocate(requests, FIXED_SEED));
        }
    }

    /// <summary> 접속 순서가 달라도 같은 사람들이면 같은 배분이 나온다 </summary>
    [Test]
    public void JoinOrderDoesNotChangeResult()
    {
        var joined = new[]
        {
            Preferring(10UL, CharacterKind.Chasa),
            Preferring(20UL, CharacterKind.Chasa),
            Preferring(30UL, CharacterKind.Munyeo),
        };

        // 같은 세 사람이 반대 순서로 들어온 경우
        var reversed = new[] { joined[2], joined[1], joined[0] };

        for (int seed = 0; seed < SEED_SAMPLES; ++seed)
        {
            CharacterKind[] a = Allocate(joined, seed);
            CharacterKind[] b = Allocate(reversed, seed);

            Assert.AreEqual(a[0], b[2], $"seed={seed}: 참가자 10의 결과가 순서에 따라 달라졌다");
            Assert.AreEqual(a[1], b[1], $"seed={seed}: 참가자 20의 결과가 순서에 따라 달라졌다");
            Assert.AreEqual(a[2], b[0], $"seed={seed}: 참가자 30의 결과가 순서에 따라 달라졌다");
        }
    }

    /// <summary> 이어받은 역할은 추첨에 넣지 않고 그대로 돌려준다 </summary>
    [Test]
    public void HeldRoleSurvivesReallocation()
    {
        var requests = new[]
        {
            Holding(1UL, CharacterKind.Munyeo),
            Preferring(2UL, CharacterKind.Munyeo),   // 이미 남이 가진 것을 원한다
            Preferring(3UL, CharacterKind.Munyeo),
        };

        for (int seed = 0; seed < SEED_SAMPLES; ++seed)
        {
            CharacterKind[] result = Allocate(requests, seed);

            Assert.AreEqual(CharacterKind.Munyeo, result[0], $"seed={seed}에서 이어받은 역할이 바뀌었다");
            AssertOneOfEach(result);
        }
    }

    /// <summary> 아무도 고르지 않아도 셋이 서로 다른 캐릭터를 받는다 </summary>
    [Test]
    public void NoPreferenceStillFillsEveryone()
    {
        var requests = new[] { NoPreference(1UL), NoPreference(2UL), NoPreference(3UL) };

        for (int seed = 0; seed < SEED_SAMPLES; ++seed)
        {
            AssertOneOfEach(Allocate(requests, seed));
        }
    }

    /// <summary> 선호한 사람과 안 한 사람이 섞여도 선호가 먼저 반영된다 </summary>
    [Test]
    public void PreferenceWinsOverBlankSlot()
    {
        var requests = new[]
        {
            NoPreference(1UL),
            Preferring(2UL, CharacterKind.Chasa),
            NoPreference(3UL),
        };

        for (int seed = 0; seed < SEED_SAMPLES; ++seed)
        {
            CharacterKind[] result = Allocate(requests, seed);

            Assert.AreEqual(CharacterKind.Chasa, result[1], $"seed={seed}에서 유일한 선호가 무시됐다");
            AssertOneOfEach(result);
        }
    }

    /// <summary> 인원이 캐릭터 종류보다 많으면 시작하지 않는다 </summary>
    [Test]
    public void TooManyPlayersFails()
    {
        var requests = new[]
        {
            NoPreference(1UL), NoPreference(2UL), NoPreference(3UL), NoPreference(4UL),
        };

        bool ok = LobbyRoleAllocator.TryAllocate(
            requests,
            new Random(FIXED_SEED),
            false,
            out WeaponKind[] _,
            out CharacterKind[] _,
            out LobbyRoleAllocator.FailureKind failure,
            out int _);

        Assert.IsFalse(ok);
        Assert.AreEqual(LobbyRoleAllocator.FailureKind.NotEnoughWeapons, failure);
    }

    /// <summary> 검 강제는 캐릭터를 그대로 두고 무기만 바꾼다 </summary>
    [Test]
    public void ForceSwordChangesWeaponOnly()
    {
        var requests = new[]
        {
            Preferring(1UL, CharacterKind.Chasa),
            Preferring(2UL, CharacterKind.Singung),
            Preferring(3UL, CharacterKind.Munyeo),
        };

        bool ok = LobbyRoleAllocator.TryAllocate(
            requests,
            new Random(FIXED_SEED),
            true,
            out WeaponKind[] weapons,
            out CharacterKind[] characters,
            out LobbyRoleAllocator.FailureKind _,
            out int _);

        Assert.IsTrue(ok);
        AssertOneOfEach(characters);

        for (int i = 0; i < weapons.Length; ++i)
        {
            Assert.AreEqual(WeaponKind.Sword, weapons[i]);
        }
    }
}
