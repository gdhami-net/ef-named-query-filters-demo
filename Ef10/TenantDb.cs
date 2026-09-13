using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace NamedQueryFilters.Ef10;

/// <summary>
/// One SQLite in-memory database, shared by every context in a test class and
/// thrown away with it. Two tenants, each with one live and one deleted invoice,
/// so "how many rows of the other tenant came back" is a number and not a
/// description.
/// </summary>
public sealed class TenantDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public const string Acme = "acme";
    public const string Globex = "globex";

    public TenantDb()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        using var schema = new SchemaContext(Options());
        schema.Database.EnsureCreated();

        var acme = new Customer { Id = 1, TenantId = Acme, Name = "Acme Ltd" };
        var globex = new Customer { Id = 2, TenantId = Globex, Name = "Globex" };
        schema.Customers.AddRange(acme, globex);
        schema.Invoices.AddRange(
            new Invoice { Id = 1, TenantId = Acme, IsDeleted = false, Number = "A-1", Customer = acme },
            new Invoice { Id = 2, TenantId = Acme, IsDeleted = true, Number = "A-2", Customer = acme },
            new Invoice { Id = 3, TenantId = Globex, IsDeleted = false, Number = "G-1", Customer = globex },
            new Invoice { Id = 4, TenantId = Globex, IsDeleted = true, Number = "G-2", Customer = globex });
        schema.SaveChanges();
    }

    public DbContextOptions Options()
        => new DbContextOptionsBuilder().UseSqlite(_connection).Options;

    /// <summary>Builds one of the context shapes in Contexts.cs over this database.</summary>
    public TContext For<TContext>(string tenant) where TContext : TenantContext
        => (TContext)Activator.CreateInstance(typeof(TContext), Options(), tenant)!;

    public void Dispose() => _connection.Dispose();
}

public static class QueryExtensions
{
    /// <summary>
    /// The WHERE clause of the SQL EF would send, with the parameter preamble
    /// and the ORDER BY cut off. This is what the post prints.
    /// </summary>
    public static string WhereClause<T>(this IQueryable<T> query)
    {
        var sql = query.ToQueryString();
        var start = sql.IndexOf("WHERE", StringComparison.Ordinal);
        if (start < 0)
        {
            return "";
        }

        var rest = sql[start..];
        var end = rest.IndexOf("ORDER BY", StringComparison.Ordinal);
        return (end < 0 ? rest : rest[..end]).Trim();
    }

    /// <summary>The invoice numbers a query returns, in a stable order.</summary>
    public static List<string> Numbers(this IQueryable<Invoice> query)
        => query.OrderBy(i => i.Id).Select(i => i.Number).ToList();

    /// <summary>How many of the returned invoices do NOT belong to the given tenant.</summary>
    public static int RowsBelongingToAnotherTenant(this IQueryable<Invoice> query, string tenant)
        => query.Count(i => i.TenantId != tenant);
}
