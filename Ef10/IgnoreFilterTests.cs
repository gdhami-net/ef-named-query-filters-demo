using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace NamedQueryFilters.Ef10;

/// <summary>
/// Point 4 of the post: dropping one named filter and keeping the rest, versus
/// the no-argument call that drops all of them.
/// </summary>
public sealed class IgnoreFilterTests(ITestOutputHelper output) : IDisposable
{
    private readonly TenantDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Ignoring_SoftDelete_by_name_keeps_the_tenant_condition()
    {
        using var context = _db.For<TwoNamedFiltersContext>(TenantDb.Acme);

        var query = context.Invoices.IgnoreQueryFilters([TwoNamedFiltersContext.SoftDelete]);
        output.WriteLine(query.OrderBy(i => i.Id).ToQueryString());

        Assert.Equal("WHERE \"i\".\"TenantId\" = @ef_filter__CurrentTenant", query.WhereClause());
        Assert.Equal(["A-1", "A-2"], query.Numbers());
        Assert.Equal(0, query.RowsBelongingToAnotherTenant(TenantDb.Acme));
    }

    [Fact]
    public void Ignoring_Tenant_by_name_keeps_the_soft_delete_condition()
    {
        using var context = _db.For<TwoNamedFiltersContext>(TenantDb.Acme);

        var query = context.Invoices.IgnoreQueryFilters([TwoNamedFiltersContext.Tenant]);
        output.WriteLine(query.OrderBy(i => i.Id).ToQueryString());

        Assert.Equal("WHERE NOT (\"i\".\"IsDeleted\")", query.WhereClause());
        Assert.Equal(["A-1", "G-1"], query.Numbers());
    }

    [Fact]
    public void The_no_argument_call_still_drops_every_filter_including_the_tenant()
    {
        using var context = _db.For<TwoNamedFiltersContext>(TenantDb.Acme);

        var query = context.Invoices.IgnoreQueryFilters();
        output.WriteLine(query.OrderBy(i => i.Id).ToQueryString());

        Assert.Equal("", query.WhereClause());
        Assert.Equal(["A-1", "A-2", "G-1", "G-2"], query.Numbers());
        Assert.Equal(2, query.RowsBelongingToAnotherTenant(TenantDb.Acme));
    }

    [Fact]
    public void A_name_that_matches_nothing_is_accepted_silently()
    {
        using var context = _db.For<TwoNamedFiltersContext>(TenantDb.Acme);

        var query = context.Invoices.IgnoreQueryFilters(["SoftDeleted"]);   // note the typo
        var rows = query.Numbers();

        foreach (var message in context.Log)
        {
            output.WriteLine(message);
        }

        // No exception, no warning: both filters are still applied.
        Assert.Equal(["A-1"], rows);
        Assert.DoesNotContain(context.Log, m => m.StartsWith("warn") || m.StartsWith("fail"));
    }

    [Fact]
    public void Ignoring_one_name_applies_to_the_included_navigation_as_well()
    {
        using var context = _db.For<TwoNamedFiltersContext>(TenantDb.Acme);

        var query = context.Customers
            .Include(c => c.Invoices)
            .IgnoreQueryFilters([TwoNamedFiltersContext.SoftDelete]);

        output.WriteLine(query.OrderBy(c => c.Id).ToQueryString());

        var customer = Assert.Single(query.ToList());
        Assert.Equal(["A-1", "A-2"], customer.Invoices.OrderBy(i => i.Id).Select(i => i.Number).ToList());
    }
}
