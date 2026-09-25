namespace Narwal.Permission.Domain;

public sealed class Role
{
    private Role()
    {
    }

    internal Role(string code, string name)
    {
        Code = RolePermissionCode.Normalize(code);
        Name = RolePermissionCode.NormalizeName(name);
    }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    internal void Rename(string name)
    {
        Name = RolePermissionCode.NormalizeName(name);
    }
}
