using System.Diagnostics.CodeAnalysis;

namespace Narwal.Permission.Services;

/// <summary>Provides the optional user ID of the actor initiating a manager operation.</summary>
public interface IRolePermissionAuditActorProvider<TUserId>
    where TUserId : notnull
{
    /// <summary>Tries to get the current actor's user ID.</summary>
    /// <param name="actorUserId">The actor ID when one is available.</param>
    /// <returns><see langword="true"/> when the actor ID is available.</returns>
    bool TryGetActorUserId([MaybeNullWhen(false)] out TUserId actorUserId);
}
