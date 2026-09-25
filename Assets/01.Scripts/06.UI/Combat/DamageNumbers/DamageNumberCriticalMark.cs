namespace Geumeum.UI
{
    /// <summary>
    /// 치명 숫자에 붙이는 작은 표식(COMBAT_STYLE_DESIGN S10 일반/치명). 색만으로 구분하지 않는다
    /// 글꼴에 ◆가 이미 있을 때만 앞에 붙이고, 없으면 뒤에 !를 붙인다
    /// 글꼴에 글자를 새로 넣지 않는다. 동적 글꼴 에셋에 글자를 추가하면 공유 에셋 파일이 바뀐다
    /// </summary>
    public readonly struct DamageNumberCriticalMark
    {
        /****************************************
        *              Constants
        ****************************************/

        public const char   GLYPH           = '◆';
        public const string GLYPH_PREFIX    = "<size=70%>◆</size>";
        public const string FALLBACK_SUFFIX = "!";

        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 숫자 앞에 붙일 글자. 없으면 null </summary>
        public readonly string Prefix;

        /// <summary> 숫자 뒤에 붙일 글자. 없으면 null </summary>
        public readonly string Suffix;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> ◆ 대신 !를 쓰고 있는지 </summary>
        public bool IsFallback => (Suffix != null);

        /****************************************
        *              Constructor
        ****************************************/

        private DamageNumberCriticalMark(string prefix, string suffix)
        {
            Prefix = prefix;
            Suffix = suffix;
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> hasGlyph는 글꼴(대체 글꼴 포함)에 ◆가 이미 들어 있는지. 확인할 때 글자를 추가하지 않는다 </summary>
        public static DamageNumberCriticalMark Resolve(bool hasGlyph)
        {
            return (hasGlyph)
                ? new DamageNumberCriticalMark(GLYPH_PREFIX, null)
                : new DamageNumberCriticalMark(null, FALLBACK_SUFFIX);
        }
    }
}
