// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 타이밍 섬광의 소리 층(S4: 색 + 모양 + 소리). 패링 가능은 맑은 고음, 불가는 낮은 경고음
    /// 비대상은 원격 그룹(isLocalPlayer = false)으로 보내 믹서의 원격 감쇠를 받는다
    /// 카탈로그에 클립이 없으면 SfxPlayer가 키마다 한 번 경고하고 넘어간다
    /// </summary>
    public static class ParryFlashSound
    {
        /****************************************
        *              Public API
        ****************************************/

        public static void Play(ParryFlashKind kind, bool isTarget, Vector3 position, uint emitterId)
        {
            SoundKey key = (kind == ParryFlashKind.Unparryable) ? SoundKey.UnparryableWarning : SoundKey.ParryWindowFlash;
            SfxPlayer.Play(key, position, isTarget, emitterId);
        }
    }
}
