namespace MianMianPet.Models;

public sealed class PetPersona
{
    private readonly string _template;

    public PetPersona(string template)
    {
        _template = template;
    }

    public string Render(AppSettings settings) => _template
        .Replace("{PetName}", settings.PetName, StringComparison.Ordinal)
        .Replace("{UserTitle}", settings.UserTitle, StringComparison.Ordinal);
}
