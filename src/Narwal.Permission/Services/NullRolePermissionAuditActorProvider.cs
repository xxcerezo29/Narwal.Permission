namespace Narwal.Permission.Services;

internal sealed class NullRolePermissionAuditActorProvider<TUserId>
    : IRolePermissionAuditActorProvider<TUserId>
    where TUserId : notnull
{
    public bool TryGetActorUserId(out TUserId actorUserId)
    {
        actorUserId = default!;
        return false;
    }
}
