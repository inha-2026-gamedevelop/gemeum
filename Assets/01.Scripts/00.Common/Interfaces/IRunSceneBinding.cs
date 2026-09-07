namespace Geumeum.Common
{
    /// <summary> 씬이 바뀔 때 자기 참조를 다시 잡는 계약 </summary>
    public interface IRunSceneBinding
    {
        /// <summary> 도착한 씬에 자기 참조와 구독을 건다. 같은 세대로 두 번 불려도 결과가 같아야 한다 </summary>
        void Bind(in SceneTransitionSnapshot transition);

        /// <summary> 떠나는 씬에서 참조와 구독을 푼다 </summary>
        void Unbind();
    }
}
