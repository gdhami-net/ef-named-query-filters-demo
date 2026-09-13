using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace NamedQueryFilters.Ef10;

/// <summary>
/// Point 6 of the post: how the filter reaches the tenant id decides whether the
/// value is a per-instance SQL parameter or a constant frozen into the model
/// cache.
/// </summary>
public sealed class TenantCaptureTests(ITestOutputHelper output) : IDisposable
{
    private readonly TenantDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Reading_the_tenant_off_the_context_gives_a_parameter_per_instance()
    {
        using var acme = _db.For<TwoNamedFiltersContext>(TenantDb.Acme);
        using var globex = _db.For<TwoNamedFiltersContext>(TenantDb.Globex);

        output.WriteLine(acme.Invoices.OrderBy(i => i.Id).ToQueryString());
        output.WriteLine(globex.Invoices.OrderBy(i => i.Id).ToQueryString());

        // Same SQL text for both, with the tenant sent as a parameter.
        Assert.Equal(acme.Invoices.WhereClause(), globex.Invoices.WhereClause());
        Assert.Contains("@ef_filter__CurrentTenant", acme.Invoices.WhereClause());

        Assert.Equal(["A-1"], acme.Invoices.Numbers());
        Assert.Equal(["G-1"], globex.Invoices.Numbers());
    }

    [Fact]
    public void The_parameter_is_named_after_the_captured_member_not_the_filter()
    {
        using var context = _db.For<TwoNamedFiltersContext>(TenantDb.Acme);

        // The filter is called "Tenant"; the property it reads is CurrentTenant.
        Assert.Contains("@ef_filter__CurrentTenant", context.Invoices.ToQueryString());
        Assert.DoesNotContain("@ef_filter__Tenant ", context.Invoices.ToQueryString());
    }

    [Fact]
    public void A_local_copy_of_the_tenant_is_frozen_into_the_cached_model()
    {
        // This context type is used by this test and nothing else, so the
        // instance that builds the model first is the one below.
        using var acme = _db.For<LocalCaptureContext>(TenantDb.Acme);
        var firstSql = acme.Invoices.OrderBy(i => i.Id).ToQueryString();
        output.WriteLine(firstSql);

        using var globex = _db.For<LocalCaptureContext>(TenantDb.Globex);
        var secondSql = globex.Invoices.OrderBy(i => i.Id).ToQueryString();
        output.WriteLine(secondSql);

        // No parameter: the tenant is a literal in the SQL, and the model was
        // cached the first time it was built.
        Assert.Contains("'acme'", firstSql);
        Assert.Contains("WHERE \"i\".\"TenantId\" = 'acme' AND NOT (\"i\".\"IsDeleted\")", firstSql);
        Assert.DoesNotContain("@ef_filter", firstSql);

        // The second context asked for globex and got acme's filter and rows.
        Assert.Equal(firstSql, secondSql);
        Assert.Equal(["A-1"], globex.Invoices.Numbers());
        Assert.Equal(1, globex.Invoices.RowsBelongingToAnotherTenant(TenantDb.Globex));
    }

    [Fact]
    public void An_IEntityTypeConfiguration_can_reach_the_tenant_through_an_unassigned_field()
    {
        using var acme = _db.For<ConfigurationSplitContext>(TenantDb.Acme);
        using var globex = _db.For<ConfigurationSplitContext>(TenantDb.Globex);

        output.WriteLine(acme.Invoices.OrderBy(i => i.Id).ToQueryString());

        Assert.Contains("@ef_filter__CurrentTenant", acme.Invoices.WhereClause());
        Assert.Equal(["A-1"], acme.Invoices.Numbers());
        Assert.Equal(["G-1"], globex.Invoices.Numbers());
    }
}
