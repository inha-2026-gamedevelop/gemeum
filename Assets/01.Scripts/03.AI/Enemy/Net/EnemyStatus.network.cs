// 서드파티
using Mirror;

namespace Geumeum.AI
{
    public partial class EnemyStatus
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar(hook = nameof(OnHealthSynced))] private float _health;

        // 원격 화면의 잡귀 HP바가 비율을 구하려면 최대 체력이 필요하다. 몰이 강화로 바뀌어도 같이 따라간다
        [SyncVar] private float _maxHealth;
    }
}
