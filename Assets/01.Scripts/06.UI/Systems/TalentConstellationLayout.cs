// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.UI
{
    /// <summary>
    /// 특성 별자리의 자리 계산. 가운데 그믐에서 파티 순번마다 가지 하나가 뻗고,
    /// 가지 끝의 뿌리에서 갈래가 부채꼴로 갈라진다
    ///
    /// 좌표는 특성 페이지 가운데 기준 px(1280x720)이다. 모양은 인스펙터에서 다듬는다
    /// </summary>
    [Serializable]
    public sealed class TalentConstellationLayout
    {
        /****************************************
        *                Fields
        ****************************************/

        // 가지 방향이 정해지지 않은 순번은 위로 뻗는다
        private const float FALLBACK_ARM_ANGLE = 90f;
        private const float RIGHT_ANGLE        = 90f;

        [Tooltip("그믐 자리. 특성 페이지 가운데 기준 px")]
        [SerializeField] private Vector2 _center = new Vector2(0f, -60f);

        [Tooltip("파티 순번(1P, 2P, 3P)마다 가지가 뻗는 방향(도). 0이 오른쪽, 90이 위")]
        [SerializeField] private float[] _armAngles = { 90f, 195f, -15f };

        [Tooltip("그믐에서 첫 노드까지 거리(px)")]
        [SerializeField] private float _rootDistance = 110f;

        [Tooltip("같은 줄에서 노드 사이 거리(px)")]
        [SerializeField] private float _nodeSpacing = 62f;

        [Tooltip("갈래 부채꼴의 반각(도). 가운데 갈래는 가지 방향을 그대로 따른다")]
        [SerializeField] private float _branchSpread = 34f;

        [Tooltip("노드를 줄에서 번갈아 비키는 거리(px). 곧은 선이 아니라 별자리처럼 보이게 한다")]
        [SerializeField] private float _zigzag = 9f;

        [Tooltip("마지막 노드 너머로 이름을 띄우는 거리(px)")]
        [SerializeField] private float _labelDistance = 34f;

        [Tooltip("가지 주인 이름표를 뿌리 옆으로 띄우는 거리. 이름 거리의 몇 배인지")]
        [SerializeField] private float _armLabelOffset = 3f;

        [Header("노드 크기(px)")]
        [Tooltip("누르고 가리키는 영역. 별이 작아도 이만큼은 잡힌다")]
        [SerializeField] private float _hitSize       = 36f;
        [SerializeField] private float _allocatedSize = 44f;
        [SerializeField] private float _openSize      = 20f;
        [SerializeField] private float _lockedSize    = 12f;
        [SerializeField] private float _otherSize     = 10f;

        [Tooltip("남의 가지 뿌리 별 크기(px). 가지 모양을 알아보는 기준점이다")]
        [SerializeField] private float _otherRootSize = 18f;
        [SerializeField] private float _lineWidth     = 2f;

        /****************************************
        *              Properties
        ****************************************/

        public Vector2 Center => _center;
        public int ArmCount => (_armAngles == null) ? 0 : _armAngles.Length;

        public float HitSize       => _hitSize;
        public float AllocatedSize => _allocatedSize;
        public float OpenSize      => _openSize;
        public float LockedSize    => _lockedSize;
        public float OtherSize     => _otherSize;
        public float OtherRootSize => _otherRootSize;
        public float LineWidth     => _lineWidth;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 노드 자리. 0열은 그믐에서 뻗는 줄기이고, 나머지 열이 줄기 끝에서 갈라지는 갈래다
        /// columnCount는 그 무기의 열 수, trunkRows는 0열 노드 수다
        /// </summary>
        public Vector2 NodePosition(int arm, int column, int row, int columnCount, int trunkRows)
        {
            float armAngle = ArmAngle(arm);
            if (column == 0)
            {
                return _center + (Direction(armAngle) * (_rootDistance + (row * _nodeSpacing))) +
                    (Perpendicular(armAngle) * Zigzag(row + 1));
            }

            float branchAngle = BranchAngle(armAngle, column, columnCount);
            return TrunkEnd(armAngle, trunkRows) +
                (Direction(branchAngle) * (_nodeSpacing * (row + 1))) +
                (Perpendicular(branchAngle) * Zigzag(row));
        }

        /// <summary> 갈래 이름 자리. 끝 노드 너머다 </summary>
        public Vector2 BranchLabelPosition(int arm, int column, int rowCount, int columnCount,
            int trunkRows)
        {
            float armAngle = ArmAngle(arm);
            float branchAngle = BranchAngle(armAngle, column, columnCount);
            return TrunkEnd(armAngle, trunkRows) +
                (Direction(branchAngle) * ((_nodeSpacing * rowCount) + _labelDistance));
        }

        /// <summary>
        /// 가지 주인 이름 자리. 뿌리의 오른편(가지 방향 기준)이다
        /// 왼편은 이웃 가지의 첫 갈래가 지나가므로 비워 둔다
        /// </summary>
        public Vector2 ArmLabelPosition(int arm)
        {
            float armAngle = ArmAngle(arm);
            return _center + (Direction(armAngle) * _rootDistance) -
                (Perpendicular(armAngle) * _labelDistance * _armLabelOffset);
        }

        private float ArmAngle(int arm)
        {
            return ((arm >= 0) && (arm < ArmCount)) ? _armAngles[arm] : FALLBACK_ARM_ANGLE;
        }

        private Vector2 TrunkEnd(float armAngle, int trunkRows)
        {
            int last = Mathf.Max(0, trunkRows - 1);
            return _center + (Direction(armAngle) * (_rootDistance + (last * _nodeSpacing)));
        }

        private float BranchAngle(float armAngle, int column, int columnCount)
        {
            int branchCount = columnCount - 1;
            if (branchCount <= 1)
            {
                return armAngle;
            }

            // 1열이 가지 방향의 왼편(반시계), 마지막 열이 오른편이 되게 편다
            float t = (float)(column - 1) / (branchCount - 1);
            return armAngle + Mathf.Lerp(_branchSpread, -_branchSpread, t);
        }

        private float Zigzag(int step)
        {
            return ((step & 1) == 0) ? _zigzag : -_zigzag;
        }

        private static Vector2 Direction(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        private static Vector2 Perpendicular(float degrees)
        {
            return Direction(degrees + RIGHT_ANGLE);
        }
    }
}
