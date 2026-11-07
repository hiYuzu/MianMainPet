namespace MianMianPet.Models;

public sealed class AppSettings
{
    public const int MaximumNameLength = 32;
    public const int MaximumTitleLength = 32;

    public string PetName { get; set; } = "绵绵";
    public string UserTitle { get; set; } = "小静";
    public bool StartWithWindows { get; set; }
    public bool AlwaysOnTop { get; set; } = true;

    public AppSettings Normalize()
    {
        var petName = string.IsNullOrWhiteSpace(PetName) ? "绵绵" : PetName.Trim();
        var userTitle = string.IsNullOrWhiteSpace(UserTitle) ? "小静" : UserTitle.Trim();
        if (petName.Length > MaximumNameLength || userTitle.Length > MaximumTitleLength)
            throw new InvalidDataException("小羊名称和对你的称呼最多各 32 个字符。");
        return new AppSettings
        {
            PetName = petName,
            UserTitle = userTitle,
            StartWithWindows = StartWithWindows,
            AlwaysOnTop = AlwaysOnTop
        };
    }
}
