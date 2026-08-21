using System;
using UnityEngine;

namespace DigitalHuman
{
    [Serializable]
    public sealed class ConversationMessage
    {
        public const string SystemRole = "system";
        public const string UserRole = "user";
        public const string AssistantRole = "assistant";

        [SerializeField] private string role;
        [SerializeField] private string content;

        public string Role => role;
        public string Content => content;

        public ConversationMessage(string messageRole, string messageContent)
        {
            role = messageRole ?? string.Empty;
            content = messageContent ?? string.Empty;
        }
    }
}
