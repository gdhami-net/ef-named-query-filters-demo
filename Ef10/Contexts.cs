using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;

namespace NamedQueryFilters.Ef10;

/// <summary>
/// Everything the contexts below share: the current tenant, the two DbSets, and
/// a log sink so a test can assert what EF did or did not say.
/// </summary>
public abstract class TenantContext(DbContextOptions options, string currentTenant) : DbContext(options)
{
    public string CurrentTenant { get; } = currentTenant;

    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Customer> Customers => Set<Customer>();

    /// <summary>Every log message EF produced on this context, at Trace and above.</summary>
    public List<string> Log { get; } = [];

    protected override void OnConfiguring(DbContextOptionsBuilder builder)
        => builder.LogTo(Log.Add, LogLevel.Trace);

    /// <summary>
    /// The "soft delete is cross-cutting so it goes in a loop" half of the story:
    /// one pass over every ISoftDelete entity type, adding !IsDeleted.
    /// </summary>
    protected static void AddSoftDeleteFilters(ModelBuilder modelBuilder, string? name)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(e => typeof(ISoftDelete).IsAssignableFrom(e.ClrType)))
        {
            var entity = Expression.Parameter(entityType.ClrType, "e");
            var notDeleted = Expression.Lambda(
                Expression.Not(Expression.Property(entity, nameof(ISoftDelete.IsDeleted))),
                entity);

            var builder = modelBuilder.Entity(entityType.ClrType);
            if (name is null)
            {
                builder.HasQueryFilter(notDeleted);
            }
            else
            {
                builder.HasQueryFilter(name, notDeleted);
            }
        }
    }
}

/// <summary>Creates the schema and holds the seed data. No query filters at all.</summary>
public sealed class SchemaContext(DbContextOptions options) : TenantContext(options, "schema");

/// <summary>
/// The shape the post is about. A tenant filter in one place, a soft-delete
/// filter in another, both unnamed. The second call wins.
/// </summary>
public sealed class TwoUnnamedFiltersContext(DbContextOptions options, string currentTenant)
    : TenantContext(options, currentTenant)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>().HasQueryFilter(i => i.TenantId == CurrentTenant);
        modelBuilder.Entity<Customer>().HasQueryFilter(c => c.TenantId == CurrentTenant);

        AddSoftDeleteFilters(modelBuilder, name: null);
    }
}

/// <summary>The same two calls in the other order, to show which one survives.</summary>
public sealed class TwoUnnamedFiltersReversedContext(DbContextOptions options, string currentTenant)
    : TenantContext(options, currentTenant)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        AddSoftDeleteFilters(modelBuilder, name: null);

        modelBuilder.Entity<Invoice>().HasQueryFilter(i => i.TenantId == CurrentTenant);
        modelBuilder.Entity<Customer>().HasQueryFilter(c => c.TenantId == CurrentTenant);
    }
}

/// <summary>The pre-EF-10 way to keep both conditions: one expression.</summary>
public sealed class OneCombinedFilterContext(DbContextOptions options, string currentTenant)
    : TenantContext(options, currentTenant)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>()
            .HasQueryFilter(i => i.TenantId == CurrentTenant && !i.IsDeleted);
        modelBuilder.Entity<Customer>()
            .HasQueryFilter(c => c.TenantId == CurrentTenant && !c.IsDeleted);
    }
}

/// <summary>The same two places, both filters named. Both survive.</summary>
public sealed class TwoNamedFiltersContext(DbContextOptions options, string currentTenant)
    : TenantContext(options, currentTenant)
{
    public const string Tenant = "Tenant";
    public const string SoftDelete = "SoftDelete";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>().HasQueryFilter(Tenant, i => i.TenantId == CurrentTenant);
        modelBuilder.Entity<Customer>().HasQueryFilter(Tenant, c => c.TenantId == CurrentTenant);

        AddSoftDeleteFilters(modelBuilder, SoftDelete);
    }
}

/// <summary>Two filters under one name on one entity type: the second still wins.</summary>
public sealed class SameNameTwiceContext(DbContextOptions options, string currentTenant)
    : TenantContext(options, currentTenant)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>().HasQueryFilter("Tenant", i => i.TenantId == CurrentTenant);
        modelBuilder.Entity<Invoice>().HasQueryFilter("Tenant", i => i.TenantId == "nobody");
    }
}

/// <summary>A named filter and an unnamed one on the SAME entity type.</summary>
public sealed class NamedAndUnnamedOnOneEntityContext(DbContextOptions options, string currentTenant)
    : TenantContext(options, currentTenant)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>().HasQueryFilter("Tenant", i => i.TenantId == CurrentTenant);
        modelBuilder.Entity<Invoice>().HasQueryFilter(i => !i.IsDeleted);
    }
}

/// <summary>The unnamed call first, the named one second. Same outcome.</summary>
public sealed class UnnamedThenNamedOnOneEntityContext(DbContextOptions options, string currentTenant)
    : TenantContext(options, currentTenant)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>().HasQueryFilter(i => !i.IsDeleted);
        modelBuilder.Entity<Invoice>().HasQueryFilter("Tenant", i => i.TenantId == CurrentTenant);
    }
}

/// <summary>Named on one entity type, unnamed on a different one.</summary>
public sealed class NamedAndUnnamedOnDifferentEntitiesContext(DbContextOptions options, string currentTenant)
    : TenantContext(options, currentTenant)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>().HasQueryFilter("Tenant", i => i.TenantId == CurrentTenant);
        modelBuilder.Entity<Customer>().HasQueryFilter(c => c.TenantId == CurrentTenant);
    }
}

/// <summary>
/// The filter reads a LOCAL copy of the tenant instead of the context. The
/// expression no longer points at the context instance, so EF has nothing to
/// re-evaluate per instance and bakes the value into the cached model.
/// This context type is used by exactly one test, so which instance builds the
/// model first is deterministic.
/// </summary>
public sealed class LocalCaptureContext(DbContextOptions options, string currentTenant)
    : TenantContext(options, currentTenant)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var tenant = CurrentTenant;     // the mistake: a copy, not the context

        modelBuilder.Entity<Invoice>().HasQueryFilter("Tenant", i => i.TenantId == tenant);
        AddSoftDeleteFilters(modelBuilder, "SoftDelete");
    }
}

/// <summary>
/// The documented workaround for reaching the tenant from an
/// IEntityTypeConfiguration: an unassigned field of the context type, used only
/// for its shape inside the expression tree.
/// </summary>
public sealed class InvoiceTenantConfiguration : IEntityTypeConfiguration<Invoice>
{
    private readonly ConfigurationSplitContext _context = null!;

    public void Configure(EntityTypeBuilder<Invoice> builder)
        => builder.HasQueryFilter("Tenant", i => i.TenantId == _context.CurrentTenant);
}

public sealed class ConfigurationSplitContext(DbContextOptions options, string currentTenant)
    : TenantContext(options, currentTenant)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new InvoiceTenantConfiguration());
        AddSoftDeleteFilters(modelBuilder, "SoftDelete");
    }
}

/// <summary>
/// A filter on the principal of a REQUIRED navigation, and none on the
/// dependent. Include() then joins with INNER JOIN and drops dependents whose
/// principal the filter removed.
/// </summary>
public sealed class RequiredNavigationContext(DbContextOptions options, string currentTenant)
    : TenantContext(options, currentTenant)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>()
            .HasMany(c => c.Invoices).WithOne(i => i.Customer).IsRequired();

        modelBuilder.Entity<Customer>().HasQueryFilter("Tenant", c => c.TenantId == CurrentTenant);
        // deliberately nothing on Invoice
    }
}
