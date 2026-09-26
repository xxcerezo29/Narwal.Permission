using System.Reflection;
using System.Runtime.ExceptionServices;
using Narwal.Permission.Domain;

namespace Narwal.Permission.Tests.Domain;

public sealed class RolePermissionAssignmentChangeTests
{
    [Fact]
    public void User_role_assigned_factory_normalizes_code_and_converts_time_to_utc()
    {
        var localTime = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.FromHours(5));
        var change = InvokeChange<Guid>(
            "UserRoleAssigned", Guid.NewGuid(), "  POSTS.Editor ", localTime, true, Guid.Empty);

        Assert.Equal("user.role_assigned", Read<string>(change, "Action"));
        Assert.Equal("posts.editor", Read<string>(change, "RoleCode"));
        Assert.Null(Read<string?>(change, "PermissionCode"));
        Assert.True(Read<bool>(change, "HasActorUserId"));
        Assert.Equal(Guid.Empty, Read<Guid>(change, "ActorUserId"));
        Assert.Equal(localTime.ToUniversalTime(), Read<DateTimeOffset>(change, "OccurredAtUtc"));
    }

    [Fact]
    public void User_role_removed_factory_keeps_missing_actor_distinct()
    {
        var change = InvokeChange<Guid>(
            "UserRoleRemoved", Guid.NewGuid(), "reader", DateTimeOffset.UnixEpoch, false, default(Guid));

        Assert.Equal("user.role_removed", Read<string>(change, "Action"));
        Assert.Equal("reader", Read<string>(change, "RoleCode"));
        Assert.False(Read<bool>(change, "HasActorUserId"));
    }

    [Fact]
    public void User_permission_granted_factory_keeps_guid_empty_as_a_present_actor()
    {
        var change = InvokeChange<Guid>(
            "UserPermissionGranted", Guid.NewGuid(), "  POSTS.Read ", DateTimeOffset.UnixEpoch, true, Guid.Empty);

        Assert.Equal("user.permission_granted", Read<string>(change, "Action"));
        Assert.Equal("posts.read", Read<string>(change, "PermissionCode"));
        Assert.Null(Read<string?>(change, "RoleCode"));
        Assert.True(Read<bool>(change, "HasActorUserId"));
        Assert.Equal(Guid.Empty, Read<Guid>(change, "ActorUserId"));
    }

    [Fact]
    public void User_permission_revoked_factory_supports_string_user_ids()
    {
        var change = InvokeChange<string>(
            "UserPermissionRevoked", "customer-42", "posts.read", DateTimeOffset.UnixEpoch, false, null);

        Assert.Equal("user.permission_revoked", Read<string>(change, "Action"));
        Assert.Equal("customer-42", Read<string>(change, "AffectedUserId"));
        Assert.Equal("posts.read", Read<string>(change, "PermissionCode"));
        Assert.False(Read<bool>(change, "HasActorUserId"));
    }

    [Fact]
    public void Assignment_change_contract_keeps_construction_and_properties_encapsulated()
    {
        var changeType = GetChangeType<Guid>();
        Assert.Empty(changeType.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(changeType.GetProperty("Action")!.GetSetMethod());
        Assert.Null(changeType.GetProperty("OccurredAtUtc")!.GetSetMethod());
        Assert.Null(changeType.GetProperty("AffectedUserId")!.GetSetMethod());
    }

    [Fact]
    public void Factories_reject_null_affected_ids_actor_ids_and_invalid_codes()
    {
        var changeType = GetChangeType<string>();
        var stringRoleFactory = FindChangeFactory(changeType, "UserRoleAssigned");
        var guidChangeType = GetChangeType<Guid>();
        var guidRoleFactory = FindChangeFactory(guidChangeType, "UserRoleAssigned");

        Assert.Throws<ArgumentNullException>(() =>
            InvokeChange(stringRoleFactory, null, "reader", DateTimeOffset.UnixEpoch, false, null));
        Assert.Throws<ArgumentNullException>(() =>
            InvokeChange(stringRoleFactory, "customer-42", "reader", DateTimeOffset.UnixEpoch, true, null));
        Assert.Throws<ArgumentException>(() =>
            InvokeChange(guidRoleFactory, Guid.NewGuid(), "posts.*", DateTimeOffset.UnixEpoch, false, default(Guid)));
    }

    private static object InvokeChange<TUserId>(string factoryName, params object?[] arguments)
        where TUserId : notnull
    {
        var changeType = GetChangeType<TUserId>();
        var factory = FindChangeFactory(changeType, factoryName);

        return InvokeChange(factory, arguments);
    }

    private static MethodInfo FindChangeFactory(Type changeType, string factoryName) =>
        Assert.Single(changeType.GetMethods(BindingFlags.Public | BindingFlags.Static),
            method => method.Name == factoryName);

    private static object InvokeChange(MethodInfo factory, params object?[] arguments)
    {
        try
        {
            var result = factory.Invoke(null, arguments);
            Assert.NotNull(result);
            return result!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static Type GetChangeType<TUserId>()
        where TUserId : notnull
    {
        var openType = typeof(UserRole<TUserId>).Assembly.GetType(
            "Narwal.Permission.Domain.RolePermissionAssignmentChange`1");
        Assert.NotNull(openType);
        return openType!.MakeGenericType(typeof(TUserId));
    }

    private static T Read<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        var value = property!.GetValue(instance);
        return value is null ? default! : Assert.IsType<T>(value);
    }
}
