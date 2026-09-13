using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace NamedQueryFilters.Ef10;

/// <summary>
/// Point 5 of the post: a named filter and an unnamed one on the same entity
/// type is an error, and the error arrives when the model is built rather than
/// at the HasQueryFilter call.
/// </summary>
public sealed class MixingTests(ITestOutputHelper output) : IDisposable
{
    private readonly TenantDb _db = new();

    public const string ExpectedMessage =
        "Both anonymous and named query filters cannot be applied simultaneously. "
        + "Specify either an anonymous filter or one or more named filters.";

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Constructing_the_context_does_not_throw()
    {
        // OnModelCreating has not run yet at this point.
        using var context = _db.For<NamedAndUnnamedOnOneEntityContext>(TenantDb.Acme);
        Assert.Equal(TenantDb.Acme, context.CurrentTenant);
    }

    [Fact]
    public void Touching_the_model_throws_with_the_message_the_post_quotes()
    {
        using var context = _db.For<NamedAndUnnamedOnOneEntityContext>(TenantDb.Acme);

        var thrown = Assert.Throws<InvalidOperationException>(() => context.Model);
        output.WriteLine(thrown.Message);

        Assert.Equal(ExpectedMessage, thrown.Message);
    }

    [Fact]
    public void The_first_query_throws_the_same_thing()
    {
        using var context = _db.For<NamedAndUnnamedOnOneEntityContext>(TenantDb.Acme);

        var thrown = Assert.Throws<InvalidOperationException>(() => context.Invoices.Numbers());

        Assert.Equal(ExpectedMessage, thrown.Message);
    }

    [Fact]
    public void The_order_of_the_two_calls_does_not_matter()
    {
        using var context = _db.For<UnnamedThenNamedOnOneEntityContext>(TenantDb.Acme);

        var thrown = Assert.Throws<InvalidOperationException>(() => context.Model);

        Assert.Equal(ExpectedMessage, thrown.Message);
    }

    [Fact]
    public void The_exception_is_thrown_by_the_second_HasQueryFilter_call_itself()
    {
        using var context = _db.For<MixedCallCaughtContext>(TenantDb.Acme);

        // With the second call wrapped, building the model succeeds: nothing later in
        // model finalization objects. The throw came from the call.
        _ = context.Model;

        var caught = MixedCallCaughtContext.CaughtAtSecondCall;
        Assert.NotNull(caught);
        output.WriteLine(caught.StackTrace);
        Assert.Equal(ExpectedMessage, caught.Message);
        Assert.Contains("HasQueryFilter", caught.StackTrace);
    }

    [Fact]
    public void The_restriction_is_per_entity_type_not_per_model()
    {
        using var context = _db.For<NamedAndUnnamedOnDifferentEntitiesContext>(TenantDb.Acme);

        output.WriteLine(context.Invoices.ToQueryString());
        output.WriteLine(context.Customers.ToQueryString());

        Assert.Equal("WHERE \"i\".\"TenantId\" = @ef_filter__CurrentTenant", context.Invoices.WhereClause());
        Assert.Equal("WHERE \"c\".\"TenantId\" = @ef_filter__CurrentTenant", context.Customers.WhereClause());
    }
}
