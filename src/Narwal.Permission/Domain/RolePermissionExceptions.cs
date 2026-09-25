namespace Narwal.Permission.Domain;

public sealed class RolePermissionNotFoundException : InvalidOperationException
{
    public RolePermissionNotFoundException(string entityType, string code)
        : base($"{entityType} with code '{code}' was not found.")
    {
        EntityType = entityType;
        Code = code;
    }

    public string EntityType { get; }

    public string Code { get; }
}

public sealed class RolePermissionAlreadyExistsException : InvalidOperationException
{
    public RolePermissionAlreadyExistsException(string entityType, string code)
        : base($"{entityType} with code '{code}' already exists.")
    {
        EntityType = entityType;
        Code = code;
    }

    public string EntityType { get; }

    public string Code { get; }
}
