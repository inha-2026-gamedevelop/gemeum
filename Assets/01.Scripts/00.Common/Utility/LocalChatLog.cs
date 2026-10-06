// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 현재 방의 클라이언트 로그. 시스템 안내에는 UI가 접두사를 붙인다 </summary>
    public static class LocalChatLog
    {
        public const int CAPACITY = 100;
        private const double SYSTEM_REPEAT_SECONDS = 1d;
        private static readonly List<ChatMessage> _entries = new List<ChatMessage>(CAPACITY);
        private static readonly List<double> _times = new List<double>(CAPACITY);
        private static readonly IReadOnlyList<ChatMessage> _view = _entries.AsReadOnly();

        public static IReadOnlyList<ChatMessage> Entries => _view;
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _entries.Clear();
            _times.Clear();
            Changed = null;
        }

        public static void AddSystem(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { return; }
            double now = Time.realtimeSinceStartupAsDouble;
            for (int i = _entries.Count - 1; i >= 0; --i)
            {
                if (now - _times[i] >= SYSTEM_REPEAT_SECONDS) { break; }
                if (_entries[i].Channel == ChatChannel.System && _entries[i].Text == text) { return; }
            }
            Append(new ChatMessage(ChatChannel.System, string.Empty, text), now);
        }

        /// <summary> 서버가 확인한 파티 메시지만 네트워크 수신부에서 추가한다 </summary>
        public static void AddParty(string sender, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { return; }
            Append(new ChatMessage(ChatChannel.Party, sender, text), Time.realtimeSinceStartupAsDouble);
        }

        public static void Clear()
        {
            if (_entries.Count == 0) { return; }
            _entries.Clear();
            _times.Clear();
            Changed?.Invoke();
        }

        private static void Append(ChatMessage entry, double time)
        {
            if (_entries.Count == CAPACITY)
            {
                _entries.RemoveAt(0);
                _times.RemoveAt(0);
            }
            _entries.Add(entry);
            _times.Add(time);
            Changed?.Invoke();
        }
    }
}
