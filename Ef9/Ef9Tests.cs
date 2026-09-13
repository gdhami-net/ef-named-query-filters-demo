using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace NamedQueryFilters.Ef9;

// The same model and the same two filters as the EF Core 10 project, on the
// newest 9.0.x. This is the evidence behind the post's "up to EF Core 9"
// sentence: there is no named overload to call, and the second unnamed call
// wins.

public class Invoice
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "";
    public bool IsDeleted { get; set; }
    public string Number { get; set; } = "";
}

public abstract class TenantContext(DbContextOptions options, string currentTenant) : DbContext(options)
{
    public string CurrentTenant { get; } = currentTenant;
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public List<string> Log { get; } = [];

    protected override void OnConfiguring(DbContextOptionsBuilder builder)
        => builder.LogTo(Log.Add, LogLevel.Trace);
}

public sealed class SchemaContext(DbContextOptions options) : TenantContext(options, "schema");

public sealed class TwoUnnamedFiltersContext(DbContextOptions options, string currentTenant)
    : TenantContext(options, currentTenant)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>().HasQueryFilter(i => i.TenantId == CurrentTenant);
        modelBuilder.Entity<Invoice>().HasQueryFilter(i => !i.IsDeleted);
    }
}

public sealed class OneCombinedFilterContext(DbContextOptions options, string currentTenant)
    : TenantContext(options, currentTenant)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Invoice>()
            .HasQueryFilter(i => i.TenantId == CurrentTenant && !i.IsDeleted);
}

public sealed class Ef9Tests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly SqliteConnection _connection;

    public Ef9Tests(ITestOutputHelper output)
    {
        _output = output;

        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        using var schema = new SchemaContext(Options());
        schema.Database.EnsureCreated();
        schema.Invoices.AddRange(
            new Invoice { Id = 1, TenantId = "acme", IsDeleted = false, Number = "A-1" },
            new Invoice { Id = 2, TenantId = "acme", IsDeleted = true, Number = "A-2" },
            new Invoice { Id = 3, TenantId = "globex", IsDeleted = false, Number = "G-1" },
            new Invoice { Id = 4, TenantId = "globex", IsDeleted = true, Number = "G-2" });
        schema.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private DbContextOptions Options()
        => new DbContextOptionsBuilder().UseSqlite(_connection).Options;

    private T For<T>(string tenant) where T : TenantContext
        => (T)Activator.CreateInstance(typeof(T), Options(), tenant)!;

    private static List<string> Numbers(IQueryable<Invoice> query)
        => query.OrderBy(i => i.Id).Select(i => i.Number).ToList();

    [Fact]
    public void There_is_no_named_HasQueryFilter_overload_on_this_build()
    {
        var named = typeof(EntityTypeBuilder<Invoice>)
            .GetMethods()
            .Where(m => m.Name == nameof(EntityTypeBuilder<Invoice>.HasQueryFilter))
            .Where(m => m.GetParameters() is [{ ParameterType.FullName: "System.String" }, _]);

        Assert.Empty(named);

        var ignore = typeof(EntityFrameworkQueryableExtensions)
            .GetMethods()
            .Where(m => m.Name == nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters));

        Assert.All(ignore, m => Assert.Single(m.GetParameters()));
    }

    [Fact]
    public void Report_the_build_this_suite_is_running_against()
    {
        var assembly = typeof(DbContext).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        _output.WriteLine($"{assembly.GetName().Name} {version}");
        _output.WriteLine($"  {assembly.Location}");
        _output.WriteLine($"runtime {Environment.Version}");

        Assert.Equal("9.0.20", version);
    }

    [Fact]
    public void The_second_unnamed_filter_replaces_the_first_here_too()
    {
        using var context = For<TwoUnnamedFiltersContext>("acme");

        var sql = context.Invoices.OrderBy(i => i.Id).ToQueryString();
        _output.WriteLine(sql);

        Assert.Contains("WHERE NOT (\"i\".\"IsDeleted\")", sql);
        Assert.DoesNotContain("@__ef_filter__CurrentTenant_0", sql);

        Assert.Equal(["A-1", "G-1"], Numbers(context.Invoices));
        Assert.Equal(1, context.Invoices.Count(i => i.TenantId != "acme"));
    }

    [Fact]
    public void Nothing_is_logged_about_the_filter_that_was_dropped()
    {
        using var context = For<TwoUnnamedFiltersContext>("acme");

        _ = Numbers(context.Invoices);

        foreach (var message in context.Log)
        {
            _output.WriteLine(message);
        }

        Assert.NotEmpty(context.Log);
        Assert.DoesNotContain(context.Log, m => m.StartsWith("warn") || m.StartsWith("fail"));
        Assert.DoesNotContain(context.Log, m => m.Contains("query filter", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_model_holds_exactly_one_filter()
    {
        using var context = For<TwoUnnamedFiltersContext>("acme");

        var filter = context.Model.FindEntityType(typeof(Invoice))!.GetQueryFilter();

        Assert.NotNull(filter);
        Assert.Contains("IsDeleted", filter.ToString());
        Assert.DoesNotContain("TenantId", filter.ToString());
    }

    [Fact]
    public void The_one_expression_workaround_keeps_both_conditions()
    {
        using var context = For<OneCombinedFilterContext>("acme");

        var sql = context.Invoices.OrderBy(i => i.Id).ToQueryString();
        _output.WriteLine(sql);

        Assert.Contains(
            "WHERE \"i\".\"TenantId\" = @__ef_filter__CurrentTenant_0 AND NOT (\"i\".\"IsDeleted\")",
            sql);
        Assert.Equal(["A-1"], Numbers(context.Invoices));
    }

    [Fact]
    public void IgnoreQueryFilters_is_all_or_nothing_on_this_build()
    {
        using var context = For<OneCombinedFilterContext>("acme");

        Assert.Equal(["A-1", "A-2", "G-1", "G-2"], Numbers(context.Invoices.IgnoreQueryFilters()));
    }
}
