#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS
// System
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Combat;
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Systems
{
    /// <summary>
    /// 양도 테스트용 인벤토리 세팅 도구. 호스트 화면에만 뜨고 값은 서버에서만 바꾼다
    ///
    /// 장비 드랍은 15퍼센트라 손으로 모으면 시간이 오래 걸린다
    /// 받을 사람과 가득 찬 사람을 즉시 만들어야 양도 규칙을 확인할 수 있다
    /// </summary>
    public sealed class InventoryTestSetup : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int WINDOW_ID = 71731;

        // 드랍 등급 가중치를 읽을 필드 레벨. 0이면 일반이 70퍼센트라 등급이 섞여 보기 좋다
        private const int FIELD_LEVEL = 0;

        // 드랍으로 생긴 장비와 인스턴스 ID가 겹치지 않게 충분히 뒤에서 시작한다
        private const uint TEST_INSTANCE_ID_BASE = 900000u;

        private const float WINDOW_WIDTH = 340f;

        /****************************************
        *                Fields
        ****************************************/

        [Header("표시")]
        [Tooltip("이 키로 창을 켜고 끈다")]
        [SerializeField] private KeyCode _toggleKey = KeyCode.F8;

        [SerializeField] private bool _isOpen = true;

        [Header("채우기")]
        [Tooltip("채울 때 쓸 무기. 양도는 장비만 되므로 소모품은 넣지 않는다")]
        [SerializeField] private WeaponKind _fillWeapon = WeaponKind.Sword;

        private readonly List<InventoryController> _inventories = new List<InventoryController>();

        private EquipmentGenerator _generator;
        private Rect   _window  = new Rect(16f, 16f, WINDOW_WIDTH, 120f);
        private string _message = "준비됨";
        private bool   _sceneRevisionPrepared = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey))
            {
                _isOpen = !_isOpen;
            }

            PrepareSceneRevision();
        }

        /// <summary>
        /// 거래 요청 헤더는 SceneRevision이 0이면 무효다
        /// 이 값은 RunSceneDirector가 씬을 옮길 때만 올라가는데 개발 씬은 전환이 없어 0에 머문다
        /// 그래서 서버에서 한 번만 1로 올려 구매, 물약, 티켓, 양도를 시험할 수 있게 한다
        ///
        /// 전용 setter가 없어 리플렉션으로 넣는다. 이 도구는 개발 씬 전용이라 여기서만 허용한다
        /// </summary>
        private void PrepareSceneRevision()
        {
            if ((_sceneRevisionPrepared) || (!NetworkServer.active))
            {
                return;
            }

            RunSceneDirector director = RunSceneDirector.ServerInstance;
            if (director == null)
            {
                return;
            }

            _sceneRevisionPrepared = true;

            if (director.Revision.Value != 0u)
            {
                return;
            }

            var setter = typeof(RunSceneDirector).GetProperty("Network_revisionValue",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

            if (setter == null)
            {
                _message = "SceneRevision을 올릴 수 없다. 거래가 막힌다";
                return;
            }

            setter.SetValue(director, 1u);
            _message = $"SceneRevision을 {director.Revision.Value}로 올렸다";
        }

        private void OnGUI()
        {
            if ((!_isOpen) || (!NetworkServer.active))
            {
                return;
            }

            RefreshInventories();
            _window = GUILayout.Window(WINDOW_ID, _window, DrawWindow, "인벤토리 테스트 세팅");
        }

        /****************************************
        *                Methods
        ****************************************/

        private void RefreshInventories()
        {
            _inventories.Clear();

            foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
            {
                if (identity == null)
                {
                    continue;
                }

                if (identity.TryGetComponent(out InventoryController inventory))
                {
                    _inventories.Add(inventory);
                }
            }

            _inventories.Sort(CompareByNetId);
        }

        private static int CompareByNetId(InventoryController left, InventoryController right)
        {
            return left.netId.CompareTo(right.netId);
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.Label($"{_toggleKey} 로 여닫는다. 서버에서만 보인다");

            if (_inventories.Count == 0)
            {
                GUILayout.Label("플레이어가 아직 없다");
                GUI.DragWindow();
                return;
            }

            for (int i = 0; i < _inventories.Count; ++i)
            {
                InventoryController inventory = _inventories[i];
                if (inventory == null)
                {
                    continue;
                }

                GUILayout.Space(4f);
                GUILayout.Label(DescribeOwner(inventory));

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("장비 1개"))
                {
                    GiveEquipment(inventory, 1);
                }

                if (GUILayout.Button("가득"))
                {
                    GiveEquipment(inventory, InventorySnapshot.SLOT_COUNT + 1);
                }

                if (GUILayout.Button("비우기"))
                {
                    ClearSlots(inventory);
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6f);
            GUILayout.Label(_message);
            GUI.DragWindow();
        }

        private string DescribeOwner(InventoryController inventory)
        {
            string label = $"netId {inventory.netId}";

            if (inventory.TryGetComponent(out PlayerCharacter character) && character.HasCharacter)
            {
                label += $" / {character.Character}";
            }

            if (inventory.TryGetComponent(out PlayerStatus status) && (status == PlayerStatus.Local))
            {
                label += " (나)";
            }

            return label;
        }

        /// <summary>
        /// 넣을 수 있을 때까지 넣는다. 12칸을 넘겨 요청하면 Full이 돌아오고,
        /// 그 값이 곧 가득 찬 인벤토리를 만들었다는 증거가 된다
        /// </summary>
        private void GiveEquipment(InventoryController inventory, int count)
        {
            if (!EnsureGenerator())
            {
                return;
            }

            int stored = 0;
            for (int i = 0; i < count; ++i)
            {
                WeaponCombatSnapshot item = _generator.Generate(_fillWeapon, FIELD_LEVEL);
                InventoryResult result = inventory.ServerTryStoreEquipment(item, inventory.Revision);

                if (result != InventoryResult.Success)
                {
                    _message = $"netId {inventory.netId}: {stored}개 넣고 멈춤 - {result}";
                    return;
                }

                ++stored;
            }

            _message = $"netId {inventory.netId}: 장비 {stored}개 지급";
        }

        private void ClearSlots(InventoryController inventory)
        {
            inventory.ResetForNewRun();
            _message = $"netId {inventory.netId}: 칸을 모두 비웠다";
        }

        // 생성기는 하나만 두고 계속 쓴다. 매번 새로 만들면 인스턴스 ID가 1부터 다시 시작해 겹친다
        private bool EnsureGenerator()
        {
            if (_generator != null)
            {
                return true;
            }

            if (!GameDB.IsInitialized)
            {
                _message = "GameDB가 아직 준비되지 않았다";
                return false;
            }

            _generator = new EquipmentGenerator(GameDB.Equipment, GameDB.DropTable,
                new System.Random(), TEST_INSTANCE_ID_BASE);
            return true;
        }
    }
}
#endif
