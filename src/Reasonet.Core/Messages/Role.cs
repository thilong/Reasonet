using System.Text.Json.Serialization;

namespace Reasonet.Messages;

/// <summary>
/// Role of a message in the conversation.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Role
{
    System,
    User,
    Assistant,
    Tool
}
