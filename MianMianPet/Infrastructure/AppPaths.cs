namespace MianMianPet.Infrastructure;

public sealed class AppPaths
{
    public AppPaths() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MianMianPet"))
    {
    }

    internal AppPaths(string root)
    {
        Root = root;
        Logs = Path.Combine(Root, "logs");
        Cache = Path.Combine(Root, "cache");
        SettingsFile = Path.Combine(Root, "settings.json");
        ConfigFile = Path.Combine(Root, "config.json");
        CelebrationsFile = Path.Combine(Root, "celebrations.json");
        DatabaseFile = Path.Combine(Root, "mianmian.db");

        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Cache);
    }

    public string Root { get; }
    public string Logs { get; }
    public string Cache { get; }
    public string SettingsFile { get; }
    public string ConfigFile { get; }
    public string CelebrationsFile { get; }
    public string DatabaseFile { get; }
}
