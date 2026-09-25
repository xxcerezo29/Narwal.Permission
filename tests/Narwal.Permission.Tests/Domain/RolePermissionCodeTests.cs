using System.Reflection;
using RolePermissionCode = Narwal.Permission.Domain.RolePermissionCode;
using PermissionEntity = Narwal.Permission.Domain.Permission;
using RoleEntity = Narwal.Permission.Domain.Role;

namespace Narwal.Permission.Tests.Domain;

public sealed class RolePermissionCodeTests
{
    [Theory]
    [InlineData(" Posts.Edit ", "posts.edit")]
    [InlineData("ADMIN_READ", "admin_read")]
    [InlineData(" billing-export ", "billing-export")]
    [InlineData("_internal.2fa", "_internal.2fa")]
    public void Normalize_trims_and_lowercases_supported_codes(string input, string expected)
    {
        Assert.Equal(expected, RolePermissionCode.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("posts.*")]
    [InlineData("résumé.read")]
    [InlineData("two words")]
    public void Normalize_rejects_blank_or_unsupported_codes(string input)
    {
        Assert.Throws<ArgumentException>(() => RolePermissionCode.Normalize(input));
    }

    [Fact]
    public void Normalize_rejects_codes_longer_than_128_characters()
    {
        Assert.Throws<ArgumentException>(
            () => RolePermissionCode.Normalize(new string('a', 129)));
    }

    [Theory]
    [InlineData(typeof(RoleEntity))]
    [InlineData(typeof(PermissionEntity))]
    public void Role_and_permission_state_has_no_public_setters(Type entityType)
    {
        foreach (var propertyName in new[] { "Code", "Name" })
        {
            var property = entityType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);

            Assert.NotNull(property);
            Assert.Null(property!.GetSetMethod());
        }
    }
}
