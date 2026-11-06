namespace MianMianPet.Models;

public sealed class CelebrationRule
{
    public string Id { get; set; } = string.Empty;
    public string Calendar { get; set; } = "solar";
    public int Month { get; set; }
    public int Day { get; set; }
    public string Message { get; set; } = string.Empty;
}
