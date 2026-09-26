using System.Reflection;
using System.Runtime.ExceptionServices;
using Narwal.Permission.Domain;

namespace Narwal.Permission.Tests.Domain;

public sealed class UserAssignmentFactoryTests
{
    [Fact]
    public void Public_factories_create_normalized_assignments_for_string_user_ids()
    {
        var roleFactory = FindCreateFactory(typeof(UserRole<string>));
        var permissionFactory = FindCreateFactory(typeof(UserPermission<string>));
        var role = Assert.IsType<UserRole<string>>(
            InvokeCreate(roleFactory, "customer-42", "  POSTS.Editor "));
        var permission = Assert.IsType<UserPermission<string>>(
            InvokeCreate(permissionFactory, "customer-42", "  POSTS.Read "));

        Assert.Equal("customer-42", role.UserId);
        Assert.Equal("posts.editor", role.RoleCode);
        Assert.Equal("customer-42", permission.UserId);
        Assert.Equal("posts.read", permission.PermissionCode);
    }

    [Fact]
    public void Assignment_entries_keep_constructors_and_setters_encapsulated()
    {
        Assert.Empty(typeof(UserRole<Guid>).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(typeof(UserPermission<Guid>).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(typeof(UserRole<Guid>).GetProperty(nameof(UserRole<Guid>.RoleCode))!.GetSetMethod());
        Assert.Null(typeof(UserPermission<Guid>).GetProperty(nameof(UserPermission<Guid>.PermissionCode))!.GetSetMethod());
    }

    [Fact]
    public void Factories_reject_null_reference_user_ids()
    {
        var roleFactory = FindCreateFactory(typeof(UserRole<string>));
        var permissionFactory = FindCreateFactory(typeof(UserPermission<string>));

        Assert.Throws<ArgumentNullException>(() =>
            InvokeCreate(roleFactory, null, "reader"));
        Assert.Throws<ArgumentNullException>(() =>
            InvokeCreate(permissionFactory, null, "posts.read"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("posts.*")]
    public void Factories_reject_invalid_codes(string code)
    {
        var roleFactory = FindCreateFactory(typeof(UserRole<Guid>));
        var permissionFactory = FindCreateFactory(typeof(UserPermission<Guid>));

        Assert.Throws<ArgumentException>(() =>
            InvokeCreate(roleFactory, Guid.NewGuid(), code));
        Assert.Throws<ArgumentException>(() =>
            InvokeCreate(permissionFactory, Guid.NewGuid(), code));
    }

    private static MethodInfo FindCreateFactory(Type assignmentType) =>
        Assert.Single(assignmentType.GetMethods(BindingFlags.Public | BindingFlags.Static),
            method => method.Name == "Create");

    private static object? InvokeCreate(MethodInfo factory, params object?[] arguments)
    {
        try
        {
            return factory.Invoke(null, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}
