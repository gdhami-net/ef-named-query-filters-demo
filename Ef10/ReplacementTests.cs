using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace NamedQueryFilters.Ef10;

/// <summary>
/// Point 1 of the post: on EF Core 10, a second UNNAMED HasQueryFilter call for
/// the same entity type still replaces the first, and EF says nothing about it.
/// </summary>
public sealed class ReplacementTests(ITestOutputHelper output) : IDisposable
{
    private readonly TenantDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void The_second_unnamed_filter_replaces_the_first()
    {
        using var context = _db.For<TwoUnnamedFiltersContext>(TenantDb.Acme);

        var where = context.Invoices.WhereClause();
        output.WriteLine(context.Invoices.OrderBy(i => i.Id).ToQueryString());

        Assert.Equal("WHERE NOT (\"i\".\"IsDeleted\")", where);
        Assert.DoesNotContain("TenantId", where);
    }

    [Fact]
    public void The_replacement_returns_the_other_tenants_live_rows()
    {
        using var context = _db.For<TwoUnnamedFiltersContext>(TenantDb.Acme);

        Assert.Equal(["A-1", "G-1"], context.Invoices.Numbers());
        Assert.Equal(1, context.Invoices.RowsBelongingToAnotherTenant(TenantDb.Acme));
    }

    [Fact]
    public void The_call_that_wins_is_the_last_one()
    {
        using var context = _db.For<TwoUnnamedFiltersReversedContext>(TenantDb.Acme);

        var where = context.Invoices.WhereClause();
        output.WriteLine(context.Invoices.OrderBy(i => i.Id).ToQueryString());

        // Tenant filter applied second this time, so the tenant condition is the
        // one that survives and the soft-delete condition is the one lost.
        Assert.Equal("WHERE \"i\".\"TenantId\" = @ef_filter__CurrentTenant", where);
        Assert.Equal(["A-1", "A-2"], context.Invoices.Numbers());
        Assert.Equal(0, context.Invoices.RowsBelongingToAnotherTenant(TenantDb.Acme));
    }

    [Fact]
    public void Nothing_is_logged_when_a_filter_is_replaced()
    {
        using var context = _db.For<TwoUnnamedFiltersContext>(TenantDb.Acme);

        _ = context.Invoices.Numbers();

        foreach (var message in context.Log)
        {
            output.WriteLine(message);
        }

        // Logging is on at Trace, the most verbose level EF's logging has. Information
        // lines (executed commands) are there; warnings, errors and any mention of
        // the replaced filter are not.
        Assert.NotEmpty(context.Log);
        Assert.DoesNotContain(context.Log, m => m.StartsWith("warn") || m.StartsWith("fail"));
        Assert.DoesNotContain(context.Log, m => m.Contains("replac", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(context.Log, m => m.Contains("overwr", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(context.Log, m => m.Contains("query filter", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_model_holds_exactly_one_filter_for_the_entity_type()
    {
        using var context = _db.For<TwoUnnamedFiltersContext>(TenantDb.Acme);

        var filters = context.Model.FindEntityType(typeof(Invoice))!.GetDeclaredQueryFilters();

        var filter = Assert.Single(filters);
        Assert.Null(filter.Key);                       // an unnamed filter has no name
        Assert.Contains("IsDeleted", filter.Expression!.ToString());
        Assert.DoesNotContain("TenantId", filter.Expression.ToString());
    }

    [Fact]
    public void One_combined_expression_keeps_both_conditions()
    {
        using var context = _db.For<OneCombinedFilterContext>(TenantDb.Acme);

        output.WriteLine(context.Invoices.OrderBy(i => i.Id).ToQueryString());

        Assert.Equal(
            "WHERE \"i\".\"TenantId\" = @ef_filter__CurrentTenant AND NOT (\"i\".\"IsDeleted\")",
            context.Invoices.WhereClause());
        Assert.Equal(["A-1"], context.Invoices.Numbers());
    }
}
