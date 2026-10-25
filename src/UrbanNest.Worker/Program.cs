using UrbanNest.Application.Abstractions;
using UrbanNest.Infrastructure;
using UrbanNest.Infrastructure.Tenancy;
using UrbanNest.Worker;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = Host.CreateApplicationBuilder(args);

// Data layer + notification dispatch (EF Core, OneSignal typed HttpClient, INotificationDispatcher,
// retry/worker options). Connection string + Notification/NotificationWorker config come from the
// worker's appsettings / user-secrets / environment (no hardcoding).
builder.Services.AddInfrastructure(builder.Configuration);

// The outbox is tenant-owned, so the DbContext global filter hides every community's rows from a
// fail-closed context. A worker has no HTTP principal and legitimately drains ALL communities, so it
// runs under a platform-super-admin context (Req 5.5). Registered here only — never in the API host.
// Replaces the fail-closed NullTenantContext that AddInfrastructure TryAdds by default.
builder.Services.RemoveAll<ITenantContext>();
builder.Services.AddScoped<ITenantContext, SuperAdminTenantContext>();

builder.Services.AddHostedService<OutboxWorkerService>();

// Parcel custody-SLA breach escalation sweep (Task 18.4, Req 31.2). Mirrors the outbox worker: a
// hosted background sweep that runs under the same cross-tenant super-admin context (registered
// above) and asks Parcel_Service to escalate a bounded batch of overdue in-custody parcels per pass.
// Its run tunables (poll interval / batch size) bind from the "CustodySlaWorker" section; the SLA
// DURATION is resolved per community via Config_Service, not here (no hardcoding).
builder.Services.AddOptions<CustodySlaWorkerOptions>()
    .Bind(builder.Configuration.GetSection(CustodySlaWorkerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHostedService<CustodySlaWorkerService>();

// Helpdesk ticket SLA-breach escalation sweep (Task 20.2, Req 33.3). Mirrors the custody-SLA worker:
// a hosted background sweep that runs under the same cross-tenant super-admin context (registered
// above) and asks Helpdesk_Service to escalate a bounded batch of breached assigned tickets per
// pass. Its run tunables (poll interval / batch size) bind from the "TicketSlaWorker" section; the
// per-priority SLA DURATIONS are resolved per community via Config_Service, not here (no hardcoding).
builder.Services.AddOptions<TicketSlaWorkerOptions>()
    .Bind(builder.Configuration.GetSection(TicketSlaWorkerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHostedService<TicketSlaWorkerService>();

// Preventive-maintenance work-order generation sweep (Task 21.2, Req 36.1). Mirrors the SLA workers:
// a hosted background sweep that runs under the same cross-tenant super-admin context (registered
// above) and asks Maintenance_Service to generate work orders for a bounded batch of due preventive
// plans per pass (advancing each plan's NextDueAtUtc under RowVersion so a plan can't double-generate
// for the same occurrence). Its run tunables (poll interval / batch size) bind from the
// "PreventiveMaintenanceWorker" section; the per-frequency plan INTERVALS are resolved per community
// via Config_Service, not here (no hardcoding).
builder.Services.AddOptions<PreventiveMaintenanceWorkerOptions>()
    .Bind(builder.Configuration.GetSection(PreventiveMaintenanceWorkerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHostedService<PreventiveMaintenanceWorkerService>();

// AMC-contract expiry-reminder sweep (Task 21.3, Req 37.3). Mirrors the SLA workers: a hosted
// background sweep that runs under the same cross-tenant super-admin context (registered above) and
// asks Maintenance_Service to remind a bounded batch of active AMC contracts that have entered their
// configured expiry-reminder window and not yet been reminded for their current end date (stamping an
// at-most-once marker under RowVersion so a contract is never reminded twice for the same window). Its
// run tunables (poll interval / batch size) bind from the "AmcExpiryReminderWorker" section; the
// reminder WINDOW (days before expiry) is resolved per community via Config_Service, not here (no
// hardcoding).
builder.Services.AddOptions<AmcExpiryReminderWorkerOptions>()
    .Bind(builder.Configuration.GetSection(AmcExpiryReminderWorkerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHostedService<AmcExpiryReminderWorkerService>();

var host = builder.Build();
host.Run();
