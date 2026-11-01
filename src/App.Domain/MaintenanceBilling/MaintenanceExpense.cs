using App.Domain.Common;

namespace App.Domain.MaintenanceBilling;

public class MaintenanceExpense : TenantOwnedEntity
{
    public string Category { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public DateTime SpentOnUtc { get; set; }

    public string Vendor { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;
}
