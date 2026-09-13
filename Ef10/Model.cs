namespace NamedQueryFilters.Ef10;

/// <summary>Marker the soft-delete filter loop looks for.</summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
}

public class Customer : ISoftDelete
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "";
    public bool IsDeleted { get; set; }
    public string Name { get; set; } = "";
    public List<Invoice> Invoices { get; set; } = [];
}

public class Invoice : ISoftDelete
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "";
    public bool IsDeleted { get; set; }
    public string Number { get; set; } = "";

    // Required by convention: a non-nullable FK and a non-nullable reference.
    // RequiredNavigationTests depends on that.
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
}
