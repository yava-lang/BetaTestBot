namespace BetaTestBot.Data.Entities;

public class User()
{
    public long ID { get; set; }
    public long TelegramID { get; set; }
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string Role { get; set; } = "User";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastActiveAt { get; set; }
    public ICollection<Application> Applications { get; set; } = new List<Application>();
}