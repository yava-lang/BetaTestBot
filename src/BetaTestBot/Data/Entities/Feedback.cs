namespace BetaTestBot.Data.Entities;

public class Feedback
{
    public long ID { get; set; }
    public long UserID { get; set; }
    public long ProjectID { get; set; }
    public string Type { get; set; } = "Feedback";
    public string Description { get; set; } = string.Empty;
    public string? AttachmentUrl { get; set; }
    public string Status { get; set; } = "Sent"; // Sent, Reviewed (for feedback), Fixed/Rejected (for bugfix)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? AdminComment { get; set; }

    public User User { get; set; } = null!;
    public Project Project { get; set; } = null!;
}