using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace NamedQueryFilters.Ef10;

/// <summary>
/// Point 3 of the post: with names on both, the same two calls in the same two
/// places produce a WHERE with both conditions in it.
/// </summary>
public sealed class NamedFilterTests(ITestOutputHelper output) : IDisposable
{
    private readonly TenantDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Both_named_filters_reach_the_sql()
    {
        using var context = _db.For<TwoNamedFiltersContext>(TenantDb.Acme);

        output.WriteLine(context.Invoices.OrderBy(i => i.Id).ToQueryString());

        Assert.Equal(
            "WHERE \"i\".\"TenantId\" = @ef_filter__CurrentTenant AND NOT (\"i\".\"IsDeleted\")",
            context.Invoices.WhereClause());
    }

    [Fact]
    public void The_conditions_appear_in_the_order_the_filters_were_configured()
    {
        using var context = _db.For<TwoNamedFiltersReversedContext>(TenantDb.Acme);

        output.WriteLine(context.Invoices.OrderBy(i => i.Id).ToQueryString());

        // Same two filters, configured soft-delete first this time.
        Assert.Equal(
            "WHERE NOT (\"i\".\"IsDeleted\") AND \"i\".\"TenantId\" = @ef_filter__CurrentTenant",
            context.Invoices.WhereClause());
        Assert.Equal(["A-1"], context.Invoices.Numbers());
    }

    [Fact]
    public void Only_the_current_tenants_live_rows_come_back()
    {
        using var context = _db.For<TwoNamedFiltersContext>(TenantDb.Acme);

        Assert.Equal(["A-1"], context.Invoices.Numbers());
        Assert.Equal(0, context.Invoices.RowsBelongingToAnotherTenant(TenantDb.Acme));
    }

    [Fact]
    public void The_model_holds_both_filters_under_their_names()
    {
        using var context = _db.For<TwoNamedFiltersContext>(TenantDb.Acme);

        var filters = context.Model.FindEntityType(typeof(Invoice))!
            .GetDeclaredQueryFilters()
            .ToDictionary(f => f.Key!, f => f.Expression!.ToString());

        Assert.Equal(2, filters.Count);
        Assert.Contains("TenantId", filters["Tenant"]);
        Assert.Contains("IsDeleted", filters["SoftDelete"]);
    }

    [Fact]
    public void Two_filters_under_one_name_still_replace_each_other()
    {
        using var context = _db.For<SameNameTwiceContext>(TenantDb.Acme);

        output.WriteLine(context.Invoices.OrderBy(i => i.Id).ToQueryString());

        var filter = Assert.Single(context.Model.FindEntityType(typeof(Invoice))!.GetDeclaredQueryFilters());
        Assert.Equal("Tenant", filter.Key);
        Assert.Contains("nobody", filter.Expression!.ToString());
        Assert.Empty(context.Invoices.Numbers());
    }

    [Fact]
    public void The_filter_applies_to_an_included_navigation_too()
    {
        using var context = _db.For<TwoNamedFiltersContext>(TenantDb.Acme);

        var sql = context.Customers.Include(c => c.Invoices).OrderBy(c => c.Id).ToQueryString();
        output.WriteLine(sql);

        // The navigation is fetched through a subquery carrying both conditions.
        Assert.Contains(
            "WHERE \"i\".\"TenantId\" = @ef_filter__CurrentTenant AND NOT (\"i\".\"IsDeleted\")",
            sql);

        var customer = Assert.Single(context.Customers.Include(c => c.Invoices).ToList());
        Assert.Equal("Acme Ltd", customer.Name);
        Assert.Equal(["A-1"], customer.Invoices.Select(i => i.Number).ToList());
    }
}
