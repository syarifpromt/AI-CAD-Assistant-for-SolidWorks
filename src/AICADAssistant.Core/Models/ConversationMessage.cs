using System;

namespace AICADAssistant.Core.Models
{
    /// <summary>Identifies who produced a message.</summary>
    public enum MessageRole
    {
        User,
        Assistant,
        System
    }

    /// <summary>Classifies the semantic purpose of a message.</summary>
    public enum MessageType
    {
        Text,
        ImageAnalysis,
        CadPlan,
        OperationResult,
        Clarification,
        Error
    }

    /// <summary>
    /// A single entry in the conversation history between the user and the AI assistant.
    /// </summary>
    public class ConversationMessage
    {
        // ── Properties ───────────────────────────────────────────────────────────

        public MessageRole Role { get; set; }
        public MessageType Type { get; set; }
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }

        /// <summary>Optional path to an attached file (image, PDF, …).</summary>
        public string AttachedFilePath { get; set; }

        public bool IsError { get; set; }

        public bool IsUser
        {
            get { return Role == MessageRole.User; }
        }

        public bool IsAssistant
        {
            get { return Role == MessageRole.Assistant || Role == MessageRole.System; }
        }

        // ── Constructors ─────────────────────────────────────────────────────────

        /// <summary>Default constructor required for serialisation.</summary>
        public ConversationMessage() { }

        /// <summary>Constructs a plain-text message.</summary>
        public ConversationMessage(MessageRole role, MessageType type, string content)
        {
            Role = role;
            Type = type;
            Content = content ?? string.Empty;
            Timestamp = DateTime.Now;
        }

        /// <summary>Constructs a message that references an attached file.</summary>
        public ConversationMessage(MessageRole role, MessageType type, string content, string attachedFilePath)
            : this(role, type, content)
        {
            AttachedFilePath = attachedFilePath;
        }

        // ── Static factory methods ────────────────────────────────────────────────

        /// <summary>Creates a plain user text message.</summary>
        public static ConversationMessage UserText(string content)
        {
            return new ConversationMessage(MessageRole.User, MessageType.Text, content);
        }

        /// <summary>Creates a plain assistant text message.</summary>
        public static ConversationMessage AssistantText(string content)
        {
            return new ConversationMessage(MessageRole.Assistant, MessageType.Text, content);
        }

        /// <summary>Creates an error message attributed to the assistant.</summary>
        public static ConversationMessage ErrorMessage(string content)
        {
            return new ConversationMessage(MessageRole.Assistant, MessageType.Error, content)
            {
                IsError = true
            };
        }

        /// <summary>Creates a clarification request from the assistant.</summary>
        public static ConversationMessage ClarificationRequest(string content)
        {
            return new ConversationMessage(MessageRole.Assistant, MessageType.Clarification, content);
        }

        /// <summary>Creates a user image-analysis message.</summary>
        public static ConversationMessage UserImage(string description, string filePath)
        {
            return new ConversationMessage(MessageRole.User, MessageType.ImageAnalysis, description, filePath);
        }

        /// <summary>Creates an assistant CAD-plan message.</summary>
        public static ConversationMessage CadPlanMessage(string jsonPlan)
        {
            return new ConversationMessage(MessageRole.Assistant, MessageType.CadPlan, jsonPlan);
        }

        // ── Overrides ─────────────────────────────────────────────────────────────

        public override string ToString()
        {
            return string.Format("[{0}][{1}] {2}: {3}",
                Timestamp.ToString("HH:mm:ss"),
                Type,
                Role,
                Content.Length > 80 ? Content.Substring(0, 77) + "..." : Content);
        }
    }
}
