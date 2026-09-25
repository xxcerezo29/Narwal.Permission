namespace Narwal.Permission.Domain;

public static class RolePermissionCode
{
    public const int MaximumLength = 128;

    public static string Normalize(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        var normalized = code.Trim().ToLowerInvariant();
        if (normalized.Length is 0 or > MaximumLength)
        {
            throw new ArgumentException(
                $"A code must contain between 1 and {MaximumLength} characters.",
                nameof(code));
        }

        foreach (var character in normalized)
        {
            if (!IsAllowed(character))
            {
                throw new ArgumentException(
                    "A code may contain only ASCII letters, digits, dots, hyphens, and underscores.",
                    nameof(code));
            }
        }

        return normalized;
    }

    internal static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var normalized = name.Trim();
        if (normalized.Length is 0 or > 256)
        {
            throw new ArgumentException(
                "A display name must contain between 1 and 256 characters.",
                nameof(name));
        }

        return normalized;
    }

    private static bool IsAllowed(char character) =>
        character is >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '.'
            or '-'
            or '_';
}
