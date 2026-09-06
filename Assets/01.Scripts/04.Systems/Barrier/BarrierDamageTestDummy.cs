// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    /// <summary>
    /// 결계 서버 피해 tick이 실제로 IDamageable.TakeDamage를 호출하는지 눈으로 확인하는 테스트 전용 더미.
    /// Player.prefab 없이 Dev_Barrier 씬에 바로 놓고 확인할 때 쓴다 — 정식 컨텐츠가 아니라 확인 끝나면 빼도 된다
    /// </summary>
    public class BarrierDamageTestDummy : MonoBehaviour, IDamageable
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private float _maxHealth = 100f;

        private float _currentHealth;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsDead => _currentHealth <= 0f;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _currentHealth = _maxHealth;
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(10f, 10f, 320f, 30f), $"BarrierHPTest HP: {_currentHealth:F1} / {_maxHealth:F1}");
        }

        /****************************************
        *           Interface Members
        ****************************************/

        public void TakeDamage(in DamageInfo damage)
        {
            if (IsDead)
            {
                return;
            }

            _currentHealth = Mathf.Max(_currentHealth - damage.Amount, 0f);
            Debug.Log($"[BarrierDamageTestDummy] {damage.Amount:F1} 피해, 남은 HP {_currentHealth:F1}/{_maxHealth:F1}", this);
        }
    }
}
