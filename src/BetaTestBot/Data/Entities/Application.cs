namespace BetaTestBot.Data.Entities;

public class Application
{
    public long ID { get; set; }
    public long UserID { get; set; }
    public long ProjectID { get; set; }
    public string Status { get; set; } = "Draft";
    public string? CurrentStep { get; set; }
    public string? Name { get; set; }
    public string? Platform { get; set; }
    public string? Experience { get; set; }
    public string? Motivation { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }

    public User? User { get; set; }
    public Project? Project { get; set; }
}
