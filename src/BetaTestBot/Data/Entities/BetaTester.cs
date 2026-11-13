namespace BetaTestBot.Data.Entities;

public class BetaTester
{
    public long ID { get; set; }
    public long UserID { get; set; }
    public long ProjectID { get; set; }
    public string Platform { get; set; } = "PC";
    public string Group { get; set; } = "Public";
    public bool IsActive { get; set; } = true;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastBuildDownloadedAt { get; set; }
    public User? User { get; set; }
    public Project? Project { get; set; }
}
