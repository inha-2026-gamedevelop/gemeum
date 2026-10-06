namespace Geumeum.Common
{
    public enum ChatChannel
    {
        Party = 0,
        System = 1,
    }

    /// <summary> 로컬 화면에 표시할 한 줄. 채널과 발신자는 서버 수신 경로에서 결정한다 </summary>
    public readonly struct ChatMessage
    {
        public ChatChannel Channel { get; }
        public string Sender { get; }
        public string Text { get; }

        public ChatMessage(ChatChannel channel, string sender, string text)
        {
            Channel = channel;
            Sender = sender ?? string.Empty;
            Text = text ?? string.Empty;
        }
    }
}
