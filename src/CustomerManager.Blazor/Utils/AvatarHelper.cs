using MudBlazor;

namespace CustomerManager.Blazor.Utils;

/// <summary>Deterministic initials + color for customer avatars — no photo
/// upload in this system, so every avatar is derived purely from the name.</summary>
public static class AvatarHelper
{
    private static readonly Color[] Palette =
    {
        Color.Primary, Color.Secondary, Color.Info, Color.Success, Color.Warning, Color.Dark
    };

    public static string GetInitials(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant()
        };
    }

    public static Color GetColor(string seed)
    {
        var hash = seed.Aggregate(0, (acc, c) => acc * 31 + c);
        var index = Math.Abs(hash) % Palette.Length;
        return Palette[index];
    }
}
