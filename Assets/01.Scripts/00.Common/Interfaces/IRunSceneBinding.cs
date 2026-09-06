namespace Geumeum.Common
{
    /// <summary>
    /// 씬이 바뀔 때 자기 참조를 다시 잡는 계약.
    ///
    /// 세션 내내 사는 객체는 씬마다 OnStartServer를 다시 받지 않는다.
    /// 씬 싱글턴인 이벤트 버스, 씬에 놓인 마커, 카메라 대상은 전환 때마다 새로 만들어지므로
    /// 옛 것에서 풀고 새 것에 거는 자리를 명시적으로 둔다. 이것이 없으면 파괴된 객체를 붙든 채 돌아간다
    /// </summary>
    public interface IRunSceneBinding
    {
        /// <summary> 도착한 씬에 자기 참조와 구독을 건다. 같은 세대로 두 번 불려도 결과가 같아야 한다 </summary>
        void Bind(in SceneTransitionSnapshot transition);

        /// <summary> 떠나는 씬에서 참조와 구독을 푼다 </summary>
        void Unbind();
    }
}
