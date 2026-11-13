using App.Domain.Common;

namespace App.Domain.MaintenanceBilling;

public static class MaintenanceLedgerDirections
{
    public const string Debit = "debit";
    public const string Credit = "credit";

    public static bool IsValid(string value) => value is Debit or Credit;
}

public class MaintenanceExpense : TenantOwnedEntity
{
    public string Direction { get; set; } = MaintenanceLedgerDirections.Debit;

    public string Category { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public DateTime SpentOnUtc { get; set; }

    public string Vendor { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;
}
