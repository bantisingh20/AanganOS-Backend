using App.Domain.Config;

namespace App.Application.Config;

public sealed record ConfigRow(
    Guid Id,
    string Key,
    ConfigScope Scope,
    Guid? OrganizationId,
    Guid? CommunityId,
    string Value);

public sealed record UpsertConfigRequest(
    string Key,
    ConfigScope Scope,
    Guid? OrganizationId,
    Guid? CommunityId,
    string Value);
