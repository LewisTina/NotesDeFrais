namespace NotesDeFrais.Models;

public sealed record AppUserProfile(
    string Subject,
    string Email,
    string DisplayName,
    IReadOnlySet<string> Groups)
{
    public bool IsAdmin => Groups.Contains("ADMIN");

    public string RoleLabel => IsAdmin ? "Administrateur" : "Employe";
}
