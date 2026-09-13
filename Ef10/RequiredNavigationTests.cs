using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace NamedQueryFilters.Ef10;

/// <summary>
/// The caution in Microsoft's query-filter documentation, measured: a filter on
/// the principal of a REQUIRED navigation changes how many dependents an
/// Include returns, because the join is an INNER JOIN.
/// </summary>
public sealed class RequiredNavigationTests(ITestOutputHelper output) : IDisposable
{
    private readonly TenantDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Without_the_Include_every_invoice_comes_back()
    {
        using var context = _db.For<RequiredNavigationContext>(TenantDb.Acme);

        // Invoice has no filter of its own in this context.
        Assert.Equal(["A-1", "A-2", "G-1", "G-2"], context.Invoices.Numbers());
    }

    [Fact]
    public void With_the_Include_the_inner_join_drops_the_other_tenants_invoices()
    {
        using var context = _db.For<RequiredNavigationContext>(TenantDb.Acme);

        var query = context.Invoices.Include(i => i.Customer);
        output.WriteLine(query.OrderBy(i => i.Id).ToQueryString());

        Assert.Contains("INNER JOIN", query.ToQueryString());
        Assert.Contains("WHERE \"c\".\"TenantId\" = @ef_filter__CurrentTenant", query.ToQueryString());

        // Materialised, not projected: a Select() would drop the Include and
        // with it the join this test is about.
        Assert.Equal(["A-1", "A-2"], query.OrderBy(i => i.Id).ToList().Select(i => i.Number));
    }
}
