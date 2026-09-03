// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 로컬 풀에 넣어도 되는 프리팹임을 선언하는 표식.
    /// 이것이 없으면 풀이 받지 않는다 - 수명을 서버가 가진 개체가 로컬 풀에 섞이면
    /// 클라이언트마다 다른 시점에 사라진다
    /// </summary>
    [DisallowMultipleComponent]
    public class LocalPoolRoot : MonoBehaviour
    {
    }
}
