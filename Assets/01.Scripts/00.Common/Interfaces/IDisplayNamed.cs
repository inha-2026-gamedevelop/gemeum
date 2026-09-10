namespace Geumeum.Common
{
    /// <summary>
    /// 로그와 화면 표시에 쓸 사람이 읽는 이름의 계약.
    ///
    /// 로그가 netId나 connectionId만 찍으면 3인 테스트에서 어느 줄이 누구 것인지
    /// 세 화면을 번갈아 보며 맞춰야 한다. 개발 도구(Geumeum.Dev)와 네트워크 계층이
    /// 전투 파트의 구체 타입을 몰라도 이름을 얻게 하려고 여기에 둔다
    /// </summary>
    public interface IDisplayNamed
    {
        /// <summary> 비어 있을 수 있다. 그때는 부르는 쪽이 대체 문구를 정한다 </summary>
        string DisplayName { get; }
    }
}
