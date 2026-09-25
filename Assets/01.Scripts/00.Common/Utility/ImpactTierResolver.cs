namespace Geumeum.Common
{
    /// <summary>
    /// 피해 결과를 표현 단계로 옮긴다(COMBAT_STYLE_DESIGN 5·5.1장). 순수 함수라 표현과 숫자 UI가 같은 답을 얻는다
    /// 같은 타격에 3타·치명·처치가 겹치면 가장 높은 단계 하나를 고른다
    /// </summary>
    public static class ImpactTierResolver
    {
        /// <summary> 받아베기가 T3이 되는 최소 표식 수 </summary>
        public const byte COUNTER_HEAVY_MARKS = 2;

        public static ImpactTier Resolve(in DamageResult result)
        {
            // 보스는 처치 강조(T4) 대상이 아니다. 보스 쓰러짐은 별도 연출이다
            if (result.IsKill && !result.TargetIsBoss)
            {
                return ImpactTier.Kill;
            }

            ImpactTier bySource = ResolveSource(result.Source, result.Detail);

            if (result.IsCritical && (bySource < ImpactTier.Heavy))
            {
                return ImpactTier.Heavy;
            }

            return bySource;
        }

        public static ImpactTier ResolveSource(DamageSourceKind source, byte detail)
        {
            switch (source)
            {
                case DamageSourceKind.CounterSlash:
                    return (detail >= COUNTER_HEAVY_MARKS) ? ImpactTier.Heavy : ImpactTier.Medium;

                case DamageSourceKind.PrecisionShot:
                case DamageSourceKind.EchoBurst:
                    return ImpactTier.Heavy;

                case DamageSourceKind.MeleeFinisher:
                case DamageSourceKind.SwordSkill:
                case DamageSourceKind.BowStab:
                case DamageSourceKind.WedgeBurst:
                case DamageSourceKind.BellField:
                    return ImpactTier.Medium;

                default:
                    return ImpactTier.Light;
            }
        }
    }
}
