// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Geumeum.AI
{
    public partial class CurseProjectile
    {
        /****************************************
        *                Fields
        ****************************************/

        // 클라이언트가 스스로 같은 직선을 그리도록 발사 조건만 보낸다
        [SyncVar] private Vector3 _direction;
        [SyncVar] private float   _speed;
    }
}
