// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.AI;
using Geumeum.Data;

namespace Geumeum.Art
{
    /// <summary>
    /// BossController.PatternImpact·PatternTelegraphVfx 신호를 받아 지점에 패턴별 VFX 프리팹을
    /// 생성했다가 지운다. 판정에는 관여하지 않는 표현 전용 러프 연출 — 1페이즈 4템플릿 전부 붙였다.
    /// 정식 프로덕션 파이프라인(PresentationConfigSO·풀링)을 거치지 않는 임시 구현이다
    /// </summary>
    [RequireComponent(typeof(BossController))]
    public sealed class BossPatternImpactVfxView : MonoBehaviour
    {
        [Serializable]
        private struct VfxEntry
        {
            public BossPatternSO.PatternTemplate Template;
            public GameObject                    Prefab;
        }

        [SerializeField] private VfxEntry[] _vfxByTemplate;
        [SerializeField] private VfxEntry[] _vfxByTemplateOnTelegraph;
        [SerializeField] private float      _vfxLifetimeSeconds = 2f;

        private BossController _controller;

        private void Awake()
        {
            _controller = GetComponent<BossController>();
        }

        private void OnEnable()
        {
            _controller.PatternImpact        += HandlePatternImpact;
            _controller.PatternTelegraphVfx  += HandlePatternTelegraphVfx;
        }

        private void OnDisable()
        {
            _controller.PatternImpact        -= HandlePatternImpact;
            _controller.PatternTelegraphVfx  -= HandlePatternTelegraphVfx;
        }

        private void HandlePatternImpact(BossPatternSO.PatternTemplate template, Vector3 origin, Vector3 forward)
        {
            SpawnVfx(ResolvePrefab(_vfxByTemplate, template), origin, forward);
        }

        private void HandlePatternTelegraphVfx(BossPatternSO.PatternTemplate template, Vector3 origin, Vector3 forward)
        {
            SpawnVfx(ResolvePrefab(_vfxByTemplateOnTelegraph, template), origin, forward);
        }

        private void SpawnVfx(GameObject prefab, Vector3 origin, Vector3 forward)
        {
            if (prefab == null)
            {
                return;
            }

            // 소스 VFX(Piloto SB_* 계열)의 정면 축이 반대라, forward를 그대로 쓰면 반대로 나간다
            GameObject instance = Instantiate(prefab, origin, Quaternion.LookRotation(-forward));
            Destroy(instance, _vfxLifetimeSeconds);
        }

        private static GameObject ResolvePrefab(VfxEntry[] table, BossPatternSO.PatternTemplate template)
        {
            if (table == null)
            {
                return null;
            }

            for (int i = 0; i < table.Length; ++i)
            {
                if (table[i].Template == template)
                {
                    return table[i].Prefab;
                }
            }

            return null;
        }
    }
}
