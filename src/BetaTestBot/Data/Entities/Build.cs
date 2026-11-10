using static System.Runtime.InteropServices.JavaScript.JSType;
namespace BetaTestBot.Data.Entities;

public class Build
{
    public long ID { get; set; }
    public long ProjectID { get; set; }
    public required string Version { get; set; }
    public required string ChangeLog { get; set; }
    public required string FileUrl { get; set; }
    public long FileSize { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public long UploadedBy { get; set; }
    public Project? Project { get; set; }
}