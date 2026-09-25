// System
using System;

namespace Geumeum.UI
{
    /// <summary>
    /// 피해량을 화면 글자로 옮긴다(COMBAT_STYLE_DESIGN S10 숫자 형식)
    /// 정수 반올림(0.5 올림), 1 미만은 "&lt;1", 천 단위 쉼표, K/M 축약 없음
    /// 고정 버퍼에 쓰므로 할당이 없다. 표시 반올림은 원본 피해값을 바꾸지 않는다
    /// </summary>
    public static class DamageNumberFormatter
    {
        /****************************************
        *              Constants
        ****************************************/

        /// <summary> long 최대 19자리 + 쉼표 6개 + 치명 표식 여유 </summary>
        public const int BUFFER_LENGTH = 64;

        private const string LESS_THAN_ONE = "<1";
        private const char   THOUSANDS_SEPARATOR = ',';
        private const int    DIGITS_PER_GROUP    = 3;
        private const int    MAX_DIGITS          = 20;
        private const double ROUNDING_HALF       = 0.5;
        private const int    DECIMAL_BASE        = 10;

        // long으로 옮길 때 넘치지 않는 상한. 실제 피해가 이만큼 커질 일은 없다
        private const double MAX_DISPLAY_VALUE = 9.0e18;

        /****************************************
        *                Fields
        ****************************************/

        // 뒤집어 쓰는 자릿수 임시칸. 표현은 메인 스레드에서만 부른다
        private static readonly char[] _scratch = new char[MAX_DIGITS + (MAX_DIGITS / DIGITS_PER_GROUP)];

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 표시할 수 있는 피해인지. 0 이하와 NaN은 숫자를 만들지 않는다 </summary>
        public static bool IsDisplayable(float amount)
        {
            return (!float.IsNaN(amount)) && (amount > 0f);
        }

        /// <summary> 표시 정수. 1 미만이면 0을 돌려준다(글자는 "&lt;1") </summary>
        public static long RoundForDisplay(float amount)
        {
            if ((!IsDisplayable(amount)) || (amount < 1f))
            {
                return 0;
            }

            double rounded = Math.Floor((double)amount + ROUNDING_HALF);
            return (long)Math.Min(rounded, MAX_DISPLAY_VALUE);
        }

        /// <summary>
        /// 버퍼에 글자를 쓰고 길이를 돌려준다. 표시하지 않는 값이면 0
        /// prefix/suffix는 치명 표식처럼 숫자 앞뒤에 붙일 글자다. 없으면 null
        /// </summary>
        public static int Write(float amount, string prefix, string suffix, char[] buffer)
        {
            if ((buffer == null) || (!IsDisplayable(amount)))
            {
                return 0;
            }

            int length = 0;
            length = Append(prefix, buffer, length);

            if (amount < 1f)
            {
                length = Append(LESS_THAN_ONE, buffer, length);
            }
            else
            {
                length = AppendGrouped(RoundForDisplay(amount), buffer, length);
            }

            length = Append(suffix, buffer, length);
            return length;
        }

        /// <summary> 테스트·디버그용 문자열. 할당하므로 매 프레임 쓰지 않는다 </summary>
        public static string Format(float amount, string prefix = null, string suffix = null)
        {
            char[] buffer = new char[BUFFER_LENGTH];
            int length    = Write(amount, prefix, suffix, buffer);

            return new string(buffer, 0, length);
        }

        /****************************************
        *                Methods
        ****************************************/

        private static int Append(string text, char[] buffer, int length)
        {
            if (string.IsNullOrEmpty(text))
            {
                return length;
            }

            int count = Math.Min(text.Length, buffer.Length - length);
            for (int i = 0; i < count; ++i)
            {
                buffer[length + i] = text[i];
            }

            return length + count;
        }

        // 뒤에서부터 자릿수를 모아 세 자리마다 쉼표를 끼운다. 문화권 설정과 무관하게 항상 쉼표다
        private static int AppendGrouped(long value, char[] buffer, int length)
        {
            char[] digits = _scratch;
            int count = 0;
            int group = 0;

            do
            {
                if (group == DIGITS_PER_GROUP)
                {
                    digits[count++] = THOUSANDS_SEPARATOR;
                    group = 0;
                }

                digits[count++] = (char)('0' + (int)(value % DECIMAL_BASE));
                value /= DECIMAL_BASE;
                ++group;
            }
            while (value > 0);

            int writable = Math.Min(count, buffer.Length - length);
            for (int i = 0; i < writable; ++i)
            {
                buffer[length + i] = digits[count - 1 - i];
            }

            return length + writable;
        }
    }
}
