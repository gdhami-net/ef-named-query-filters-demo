using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace NamedQueryFilters.Ef10;

/// <summary>
/// The limits section of the post: which ways of reaching the database carry the
/// query filters and which do not. "Raw SQL is not filtered" is too broad, so
/// each route is measured separately.
/// </summary>
public sealed class WhatIsFilteredTests(ITestOutputHelper output) : IDisposable
{
    private readonly TenantDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void FromSql_on_a_DbSet_is_composed_over_and_stays_filtered()
    {
        using var context = _db.For<TwoNamedFiltersContext>(TenantDb.Acme);

        var query = context.Invoices.FromSql($"SELECT * FROM Invoices");
        output.WriteLine(query.OrderBy(i => i.Id).ToQueryString());

        // EF wraps the raw SQL in a subquery and puts the filters on the outside.
        // The alias EF picks is not part of any contract, so it is not asserted.
        Assert.Contains("FROM (", query.ToQueryString());
        Assert.Matches(
            """WHERE "\w+"\."TenantId" = @ef_filter__CurrentTenant AND NOT \("\w+"\."IsDeleted"\)""",
            query.WhereClause());
        Assert.Equal(["A-1"], query.Numbers());
    }

    [Fact]
    public void ExecuteUpdate_carries_the_filters_into_the_UPDATE_statement()
    {
        using var context = _db.For<TwoNamedFiltersContext>(TenantDb.Acme);
        using var transaction = context.Database.BeginTransaction();

        var affected = context.Invoices.ExecuteUpdate(s => s.SetProperty(i => i.Number, i => i.Number + "!"));

        var statement = context.Log.Last(m => m.Contains("UPDATE \"Invoices\"", StringComparison.Ordinal));
        output.WriteLine(statement);

        Assert.Equal(1, affected);      // only acme's one live invoice
        Assert.Contains("WHERE \"i\".\"TenantId\" = @ef_filter__CurrentTenant AND NOT (\"i\".\"IsDeleted\")",
            statement);

        transaction.Rollback();
    }

    [Fact]
    public void ExecuteDelete_carries_them_too()
    {
        using var context = _db.For<TwoNamedFiltersContext>(TenantDb.Acme);
        using var transaction = context.Database.BeginTransaction();

        var affected = context.Invoices.ExecuteDelete();

        Assert.Equal(1, affected);

        transaction.Rollback();
    }

    [Fact]
    public void SqlQuery_is_not_an_entity_query_and_is_not_filtered()
    {
        using var context = _db.For<TwoNamedFiltersContext>(TenantDb.Acme);

        var numbers = context.Database
            .SqlQuery<string>($"SELECT Number AS Value FROM Invoices ORDER BY Id")
            .ToList();

        output.WriteLine(string.Join(", ", numbers));

        Assert.Equal(["A-1", "A-2", "G-1", "G-2"], numbers);
    }

    [Fact]
    public void ExecuteSqlRaw_goes_straight_to_the_database()
    {
        using var context = _db.For<TwoNamedFiltersContext>(TenantDb.Acme);
        using var transaction = context.Database.BeginTransaction();

        // All four rows, both tenants, deleted ones included.
        var affected = context.Database.ExecuteSql($"UPDATE Invoices SET Number = Number");

        Assert.Equal(4, affected);

        transaction.Rollback();
    }
}
