namespace BetaTestBot.Data.Entities;

public class Project
{
    public long ID { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Build> Builds { get; set; } = new List<Build>();
    public ICollection<BetaTester> BetaTesters { get; set; } = new List<BetaTester>();
}
