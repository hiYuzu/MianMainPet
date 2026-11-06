namespace MianMianPet.Models;

public sealed class AppSettings
{
    public string PetName { get; set; } = "绵绵";
    public string UserTitle { get; set; } = "小静";
    public bool StartWithWindows { get; set; }
    public bool AlwaysOnTop { get; set; } = true;

    public AppSettings Normalize()
    {
        PetName = string.IsNullOrWhiteSpace(PetName) ? "绵绵" : PetName.Trim();
        UserTitle = string.IsNullOrWhiteSpace(UserTitle) ? "小静" : UserTitle.Trim();
        return this;
    }
}
