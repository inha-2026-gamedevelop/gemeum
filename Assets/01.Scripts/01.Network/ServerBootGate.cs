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
    /// <summary>
    /// 서버를 열기 전에 GameDB, 런 데이터, 스폰 프리팹을 검사한다.
    /// 여기서 걸러지지 않으면 결함이 런 중반에야 드러나고 3인 세션 전체가 버려진다
    /// </summary>
    public static class ServerBootGate
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버 시작 조건을 모두 만족하는지 검사한다, 실패 원인은 failures에 담는다 </summary>
        public static bool TryValidate(NetworkManager manager, List<string> failures)
        {
            failures.Clear();

            if (manager == null)
            {
                failures.Add("NetworkManager가 없다");
                return false;
            }

            ValidateGameDatabase(failures);
            ValidateTransport(manager, failures);
            ValidateAuthenticator(manager, failures);
            ValidatePlayerPrefab(manager, failures);
            ValidateSpawnPrefabs(manager, failures);
            ValidateSceneSetup(failures);

            return (failures.Count == 0);
        }

        private static void ValidateGameDatabase(List<string> failures)
        {
            if (!GameDB.IsInitialized)
            {
                failures.Add("GameDB가 초기화되지 않았다, @Managers에 GameDBBootstrap이 있는지 확인할 것");
                return;
            }

            GeumeumBootValidator.ValidateData(GameDB.Barrier, GameDB.LevelCurve, GameDB.Spawn, failures);
        }

        // Main은 로비부터 정산까지 전체 경로를 요구하고, 그 외 씬(Dev_Combat 등)은 전투 확인용 완화 프로필을 쓴다
        private static void ValidateSceneSetup(List<string> failures)
        {
            bool isMain = (SceneManager.GetActiveScene().name == Constants.Scene.MAIN);
            var profile = isMain ? GeumeumBootValidator.SceneProfile.Main : GeumeumBootValidator.SceneProfile.DevScene;
            GeumeumBootValidator.ValidateScene(profile, failures);
        }

        private static void ValidateTransport(NetworkManager manager, List<string> failures)
        {
            if (manager.transport == null)
            {
                failures.Add("트랜스포트가 비어 있다");
            }
        }

        // 트랜스포트와 신원 소스가 어긋나면 인증기가 신원을 엉뚱한 곳에서 얻는다.
        // KCP는 클라이언트가 보낸 값을, Steam은 conn.address를 쓰므로 짝이 맞아야 한다
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

            bool   isSteamTransport = (manager.transport != null) && (manager.transport.GetType().Name.Contains("Fizzy"));
            string expected         = isSteamTransport ? "Steam" : "Dev";

            if (!string.Equals(identity, expected, StringComparison.Ordinal))
            {
                failures.Add($"트랜스포트와 신원 소스의 짝이 맞지 않는다: 트랜스포트={manager.transport.GetType().Name}, 신원={identity}, 기대={expected}");
            }
        }

        private static void ValidatePlayerPrefab(NetworkManager manager, List<string> failures)
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

            // 없으면 역할 배분이 창구를 못 찾아 런이 조용히 열리지 않는다, 증상만으로는 원인을 찾기 어렵다
            if (manager.playerPrefab.GetComponent<IPlayerRoleAssignment>() == null)
            {
                failures.Add("플레이어 프리팹에 역할 확정 창구(IPlayerRoleAssignment)가 없다");
            }

            if (manager.playerPrefab.GetComponent<IPlayerRunLifecycle>() == null)
            {
                failures.Add("플레이어 프리팹에 런 경계 창구(IPlayerRunLifecycle)가 없다");
            }
        }

        private static void ValidateSpawnPrefabs(NetworkManager manager, List<string> failures)
        {
            List<GameObject> spawnPrefabs = manager.spawnPrefabs;
            if (spawnPrefabs == null)
            {
                return;
            }

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
    }
}
