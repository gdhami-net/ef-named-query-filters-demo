using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xunit;
using Xunit.Abstractions;

namespace NamedQueryFilters.Ef10;

/// <summary>
/// Prints and asserts which build actually ran, so the version numbers in the
/// post come from the test output rather than from the csproj.
/// </summary>
public sealed class VersionTests(ITestOutputHelper output)
{
    private static string InformationalVersion(Assembly assembly)
        => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    [Fact]
    public void Report_the_build_this_suite_is_running_against()
    {
        foreach (var assembly in new[] { typeof(DbContext).Assembly, typeof(SqliteDbContextOptionsBuilderExtensions).Assembly })
        {
            output.WriteLine($"{assembly.GetName().Name} {InformationalVersion(assembly)}");
            output.WriteLine($"  {assembly.Location}");
        }

        output.WriteLine($"runtime {Environment.Version}");

        Assert.Equal("10.0.12", InformationalVersion(typeof(DbContext).Assembly));
    }

    [Fact]
    public void The_named_overloads_exist_on_this_build()
    {
        var named = typeof(EntityTypeBuilder<Invoice>)
            .GetMethods()
            .Where(m => m.Name == nameof(EntityTypeBuilder<Invoice>.HasQueryFilter))
            .Where(m => m.GetParameters() is [{ ParameterType.FullName: "System.String" }, _])
            .ToList();

        Assert.NotEmpty(named);

        var byName = typeof(EntityFrameworkQueryableExtensions)
            .GetMethods()
            .Single(m => m.Name == nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters)
                         && m.GetParameters().Length == 2);

        Assert.Equal(typeof(IReadOnlyCollection<string>), byName.GetParameters()[1].ParameterType);
    }
}
