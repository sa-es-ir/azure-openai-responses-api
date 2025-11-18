namespace MessagingApp.Models;

public class Conversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastMessageAt { get; set; } = DateTime.Now;
    public List<Message> Messages { get; set; } = new();
}

public class Message
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Text { get; set; } = string.Empty;
    public bool IsFromUser { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string ConversationId { get; set; } = string.Empty;
}
