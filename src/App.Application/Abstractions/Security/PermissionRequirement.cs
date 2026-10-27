using App.Domain.Identity;

namespace App.Application.Abstractions.Security;

/// <summary>
/// A required (<see cref="Action"/>, <see cref="ResourceType"/>) permission that a protected
/// endpoint declares. The pipeline's authorization hook passes this to the
/// <see cref="IPermissionEvaluator"/> before the operation runs (Req 4.1–4.3).
/// </summary>
/// <param name="Action">The verb the caller must be permitted to perform.</param>
/// <param name="ResourceType">The resource type the action targets (e.g. "Unit", "Ticket").</param>
public readonly record struct PermissionRequirement(PermissionAction Action, string ResourceType);
