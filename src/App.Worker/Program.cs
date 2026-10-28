using App.Application.Abstractions;
using App.Infrastructure;
using App.Infrastructure.Tenancy;
using App.Worker;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.RemoveAll<ITenantContext>();
builder.Services.AddScoped<ITenantContext, SuperAdminTenantContext>();

builder.Services.AddHostedService<OutboxWorkerService>();
builder.Services.AddHostedService<App.Infrastructure.Messaging.MessageOutboxDrainService>();

builder.Services.AddOptions<CustodySlaWorkerOptions>()
    .Bind(builder.Configuration.GetSection(CustodySlaWorkerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHostedService<CustodySlaWorkerService>();

builder.Services.AddOptions<TicketSlaWorkerOptions>()
    .Bind(builder.Configuration.GetSection(TicketSlaWorkerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHostedService<TicketSlaWorkerService>();

builder.Services.AddOptions<PreventiveMaintenanceWorkerOptions>()
    .Bind(builder.Configuration.GetSection(PreventiveMaintenanceWorkerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHostedService<PreventiveMaintenanceWorkerService>();


builder.Services.AddOptions<AmcExpiryReminderWorkerOptions>()
    .Bind(builder.Configuration.GetSection(AmcExpiryReminderWorkerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHostedService<AmcExpiryReminderWorkerService>();

builder.Services.AddOptions<WalkInExpiryWorkerOptions>()
    .Bind(builder.Configuration.GetSection(WalkInExpiryWorkerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHostedService<WalkInExpiryWorkerService>();

var host = builder.Build();
host.Run();
