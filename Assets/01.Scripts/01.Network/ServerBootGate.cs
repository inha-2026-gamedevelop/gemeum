// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.SceneManagement;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Network
{
    /// <summary> 서버를 열기 전에 GameDB, 런 데이터, 스폰 프리팹을 검사한다 </summary>
    public static class ServerBootGate
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버 시작 조건을 모두 만족하는지 검사한다 </summary>
        public static bool TryValidate(NetworkManager manager, List<string> failures)
        {
            GeumeumBootValidator.SceneProfile profile =
                GeumeumBootValidator.ResolveSceneProfile(SceneManager.GetActiveScene().name);
            return TryValidate(manager, profile, failures);
        }

        /// <summary> 지정한 씬 프로필로 서버 시작 조건을 검사한다 </summary>
        public static bool TryValidate(
            NetworkManager manager,
            GeumeumBootValidator.SceneProfile profile,
            List<string> failures)
        {
            if (failures == null)
            {
                return false;
            }

            failures.Clear();

            if (manager == null)
            {
                failures.Add("NetworkManager가 없다");
                return false;
            }

            ValidateGameDatabase(profile, failures);
            ValidateTransport(manager, failures);
            ValidateAuthenticator(manager, failures);
            ValidatePlayerPrefab(manager, profile, failures);
            ValidateSpawnPrefabs(manager, profile, failures);
            ValidateSceneSetup(profile, failures);

            return (failures.Count == 0);
        }

        private static void ValidateGameDatabase(
            GeumeumBootValidator.SceneProfile profile,
            List<string> failures)
        {
            if (!GameDB.IsInitialized)
            {
                failures.Add("GameDB가 초기화되지 않았다, @Managers에 GameDBBootstrap이 있는지 확인할 것");
                return;
            }

            GeumeumBootValidator.ValidateData(
                GameDB.Barrier,
                GameDB.LevelCurve,
                GameDB.Spawn,
                GameDB.Boss,
                profile,
                failures);
            GeumeumBootValidator.ValidatePlayerServices(GameDB.Combat, GameDB.Shop, failures);
            GeumeumBootValidator.ValidateProgressionData(GameDB.Combat, GameDB.Talent,
                GameDB.Score, profile, failures);
        }

        private static void ValidateSceneSetup(
            GeumeumBootValidator.SceneProfile profile,
            List<string> failures)
        {
            GeumeumBootValidator.ValidateScene(profile, failures);
        }

        private static void ValidateTransport(NetworkManager manager, List<string> failures)
        {
            if (manager.transport == null)
            {
                failures.Add("트랜스포트가 비어 있다");
            }
        }

        private static void ValidateAuthenticator(NetworkManager manager, List<string> failures)
        {
            if (manager.authenticator == null)
            {
                failures.Add("인증기가 연결되지 않았다, 신원 없이 접속을 받게 된다");
                return;
            }

            if (!(manager.authenticator is GeumeumAuthenticator authenticator))
            {
                failures.Add($"인증기가 GeumeumAuthenticator가 아니다: {manager.authenticator.GetType().Name}");
                return;
            }

            string identity = authenticator.IdentityLabel;
            if (string.IsNullOrEmpty(identity))
            {
                failures.Add("인증기에 신원 소스가 연결되지 않았다");
                return;
            }

            bool   isSteamTransport = SteamTransportSelector.IsSteamTransport(manager.transport);
            string expected         = isSteamTransport ? "Steam" : "Dev";

            if (!string.Equals(identity, expected, StringComparison.Ordinal))
            {
                failures.Add($"트랜스포트와 신원 소스의 짝이 맞지 않는다: 트랜스포트={manager.transport.GetType().Name}, 신원={identity}, 기대={expected}");
            }
        }

        private static void ValidatePlayerPrefab(NetworkManager manager,
            GeumeumBootValidator.SceneProfile profile, List<string> failures)
        {
            if (manager.playerPrefab == null)
            {
                failures.Add("플레이어 프리팹이 등록되지 않았다");
                return;
            }

            if (manager.playerPrefab.GetComponent<NetworkIdentity>() == null)
            {
                failures.Add("플레이어 프리팹에 NetworkIdentity가 없다");
            }

            if (manager.playerPrefab.GetComponent<IPlayerRoleAssignment>() == null)
            {
                failures.Add("플레이어 프리팹에 역할 확정 창구(IPlayerRoleAssignment)가 없다");
            }

            if (manager.playerPrefab.GetComponent<IPlayerRunLifecycle>() == null)
            {
                failures.Add("플레이어 프리팹에 런 경계 창구(IPlayerRunLifecycle)가 없다");
            }

            if (profile != GeumeumBootValidator.SceneProfile.Main)
            {
                return;
            }

            if ((manager.playerPrefab.GetComponent<ITalentAllocationProvider>() == null) ||
                (manager.playerPrefab.GetComponent<ISupportBuffTarget>() == null) ||
                (manager.playerPrefab.GetComponent<IResourceTransactionTarget>() == null) ||
                (manager.playerPrefab.GetComponent<IRunResultParticipantSource>() == null) ||
                (manager.playerPrefab.GetComponent<IBossSwallowTarget>() == null))
            {
                failures.Add("플레이어 프리팹에 특성, 지원 버프, 자원 거래, 정산 참가자 또는 삼켜짐 창구가 없다");
            }
        }

        private static void ValidateSpawnPrefabs(
            NetworkManager manager,
            GeumeumBootValidator.SceneProfile profile,
            List<string> failures)
        {
            List<GameObject> spawnPrefabs = manager.spawnPrefabs;
            if (spawnPrefabs != null)
            {
                for (int i = 0; i < spawnPrefabs.Count; ++i)
                {
                    if (spawnPrefabs[i] == null)
                    {
                        failures.Add($"스폰 프리팹 {i}번이 비어 있다");
                        continue;
                    }

                    if (spawnPrefabs[i].GetComponent<NetworkIdentity>() == null)
                    {
                        failures.Add($"스폰 프리팹 {spawnPrefabs[i].name}에 NetworkIdentity가 없다");
                    }
                }
            }

            ValidateRequiredSpawnPrefabs(manager, profile, failures);
        }

        private static void ValidateRequiredSpawnPrefabs(
            NetworkManager manager,
            GeumeumBootValidator.SceneProfile profile,
            List<string> failures)
        {
            if (profile != GeumeumBootValidator.SceneProfile.Main)
            {
                return;
            }

            MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include);
            List<GameObject> requiredPrefabs = new List<GameObject>();
            bool hasProvider = false;

            for (int i = 0; i < behaviours.Length; ++i)
            {
                MonoBehaviour behaviour = behaviours[i];
                if ((behaviour == null) || !(behaviour is IRequiredSpawnPrefabProvider provider))
                {
                    continue;
                }

#if UNITY_EDITOR
                Transform current = behaviour.transform;
                bool editorOnly = false;
                while (current != null)
                {
                    if (current.CompareTag("EditorOnly"))
                    {
                        editorOnly = true;
                        break;
                    }

                    current = current.parent;
                }

                if (editorOnly)
                {
                    continue;
                }
#endif

                hasProvider = true;
                provider.CollectRequiredSpawnPrefabs(requiredPrefabs);
            }

            if (!hasProvider)
            {
                failures.Add("씬에 요구 스폰 프리팹 공급자가 없다");
                return;
            }

            if (requiredPrefabs.Count == 0)
            {
                failures.Add("씬의 요구 스폰 프리팹 공급자가 프리팹을 제공하지 않았다");
                return;
            }

            List<GameObject> spawnPrefabs = manager.spawnPrefabs;
            for (int i = 0; i < requiredPrefabs.Count; ++i)
            {
                GameObject requiredPrefab = requiredPrefabs[i];
                if (requiredPrefab == null)
                {
                    failures.Add($"요구 스폰 프리팹 {i}번이 비어 있다");
                    continue;
                }

                if ((spawnPrefabs == null) || !spawnPrefabs.Contains(requiredPrefab))
                {
                    failures.Add($"요구 스폰 프리팹 {requiredPrefab.name}이 NetworkManager.spawnPrefabs에 등록되지 않았다");
                }
            }
        }
    }
}
