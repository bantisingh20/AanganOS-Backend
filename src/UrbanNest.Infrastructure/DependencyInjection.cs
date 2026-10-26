using UrbanNest.Application.Abstractions;
using UrbanNest.Application.Abstractions.Audit;
using UrbanNest.Application.Abstractions.Idempotency;
using UrbanNest.Application.Abstractions.Security;
using UrbanNest.Application.Audit;
using UrbanNest.Application.Auth;
using UrbanNest.Application.Community;
using UrbanNest.Application.Config;
using UrbanNest.Application.Files;
using UrbanNest.Application.Gate;
using UrbanNest.Application.Helpdesk;
using UrbanNest.Application.Notifications;
using UrbanNest.Application.Parcel;
using UrbanNest.Application.Querying;
using UrbanNest.Application.Identity;
using UrbanNest.Application.Maintenance;
using UrbanNest.Application.Residents;
using UrbanNest.Application.Amenities;
using UrbanNest.Application.Parking;
using UrbanNest.Infrastructure.Amenities;
using UrbanNest.Infrastructure.Parking;
using UrbanNest.Infrastructure.Audit;
using UrbanNest.Infrastructure.Auth;
using UrbanNest.Infrastructure.Community;
using UrbanNest.Infrastructure.Config;
using UrbanNest.Infrastructure.Files;
using UrbanNest.Infrastructure.Gate;
using UrbanNest.Infrastructure.Helpdesk;
using UrbanNest.Infrastructure.Idempotency;
using UrbanNest.Infrastructure.Identity;
using UrbanNest.Infrastructure.Maintenance;
using UrbanNest.Infrastructure.Notifications;
using UrbanNest.Infrastructure.Parcel;
using UrbanNest.Infrastructure.Persistence;
using UrbanNest.Infrastructure.Residents;
using UrbanNest.Infrastructure.Persistence.Interceptors;
using UrbanNest.Infrastructure.Querying;
using UrbanNest.Infrastructure.Security;
using UrbanNest.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace UrbanNest.Infrastructure;


public static class DependencyInjection
{
    
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DbContextOptionsBuilder>? configureProvider = null)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<AuditSaveChangesInterceptor>();

        services.AddOptions<PaginationOptions>()
            .Bind(configuration.GetSection(PaginationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IAsyncQueryExecutor, EfAsyncQueryExecutor>();

        services.AddMemoryCache();
        services.AddScoped<IConfigService, ConfigService>();


        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IConfigAuditSink, AuditConfigAuditSink>();

        services.AddScoped<IPageSizePolicy, ConfigPageSizePolicy>();

        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IUserRoleService, UserRoleService>();
        services.AddScoped<IPermissionCatalog, PermissionCatalog>();
        services.AddScoped<IEffectivePermissionQuery, EffectivePermissionQuery>();
        services.AddScoped<UrbanNest.Application.Identity.IMenuService, Identity.MenuService>();
        services.AddScoped<UrbanNest.Application.Identity.IAdminUserService, Identity.AdminUserService>();

        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<ICommunityService, CommunityService>();
        services.AddScoped<IHierarchyService, HierarchyService>();

        services.AddScoped<IUnitService, UnitService>();
        services.AddOptions<BulkOptions>()
            .Bind(configuration.GetSection(BulkOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<IUnitBulkService, UnitBulkService>();

        services.AddScoped<IResidentService, ResidentService>();


        services.AddScoped<IUnitHistoryService, UnitHistoryService>();

        services.AddScoped<IResidentAssetsService, ResidentAssetsService>();


        services.AddScoped<ICommunicationPreferenceService, CommunicationPreferenceService>();
        services.AddScoped<ICommunicationPreferences, ResidentCommunicationPreferences>();


        services.AddScoped<IGateService, GateService>();

        services.AddScoped<ISecurityOpsService, SecurityOpsService>();

        services.AddScoped<IParcelService, ParcelService>();

        services.AddScoped<IHelpdeskService, HelpdeskService>();

        services.AddScoped<IMaintenanceService, MaintenanceService>();


        services.AddScoped<IAmenityService, AmenityService>();


        services.AddScoped<IParkingService, ParkingService>();

        services.AddScoped<IAnnouncementService, AnnouncementService>();

        services.TryAddScoped<IAccessControlIntegration, LoggingAccessControlIntegration>();

        services.AddScoped<AuthorizationService>();
        services.AddScoped<IPermissionEvaluator>(sp => sp.GetRequiredService<AuthorizationService>());
        services.AddScoped<IAuthorizationGuard>(sp => sp.GetRequiredService<AuthorizationService>());

        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<JwtIssuerOptions>()
            .Bind(configuration.GetSection(JwtIssuerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IFileBlobStore, LocalFileBlobStore>();
        services.AddScoped<IFileService, FileService>();

        services.AddOptions<NotificationOptions>()
            .Bind(configuration.GetSection(NotificationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<INotificationTemplateRenderer, PlaceholderTemplateRenderer>();
        services.AddScoped<INotificationRecipientResolver, DeviceRecipientResolver>();
       
        services.TryAddScoped<ICommunicationPreferences, DefaultCommunicationPreferences>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<UrbanNest.Application.Notifications.INotificationInboxService, Notifications.NotificationInboxService>();

        services.AddOptions<NotificationWorkerOptions>()
            .Bind(configuration.GetSection(NotificationWorkerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddHttpClient<IOneSignalClient, OneSignalHttpClient>((sp, client) =>
        {
            var notif = sp.GetRequiredService<IOptions<NotificationOptions>>().Value;
            client.BaseAddress = new Uri(notif.OneSignalApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(notif.HttpTimeoutSeconds);
        });
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

        services.AddOptions<UrbanNest.Application.Messaging.SmtpOptions>()
            .Bind(configuration.GetSection(UrbanNest.Application.Messaging.SmtpOptions.SectionName));
        services.AddOptions<UrbanNest.Application.Messaging.MessageOutboxWorkerOptions>()
            .Bind(configuration.GetSection(UrbanNest.Application.Messaging.MessageOutboxWorkerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<UrbanNest.Application.Messaging.IMessageSender, Messaging.MessageSender>();
        services.AddScoped<UrbanNest.Application.Messaging.IMessageOutboxQueryService, Messaging.MessageOutboxQueryService>();
        services.AddScoped<UrbanNest.Application.Notifications.INotificationOutboxQueryService, Notifications.NotificationOutboxQueryService>();
        services.AddScoped<UrbanNest.Application.Messaging.IEmailChannelSender, Messaging.SmtpEmailChannelSender>();
        services.TryAddScoped<UrbanNest.Application.Messaging.ISmsChannelSender, Messaging.NotConfiguredSmsChannelSender>();
        services.TryAddScoped<UrbanNest.Application.Messaging.IWhatsAppChannelSender, Messaging.NotConfiguredWhatsAppChannelSender>();


        services.AddOptions<IdempotencyOptions>()
            .Bind(configuration.GetSection(IdempotencyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<IIdempotencyService, IdempotencyService>();

        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();
        services.AddSingleton<IAuthTokenIssuer, JwtAuthTokenIssuer>();
        services.AddScoped<ISessionRevocationStore, SessionRevocationStore>();
        services.TryAddScoped<IOtpDeliverer, LoggingOtpDeliverer>(); // Notification_Service (Task 7.1) replaces later.
        services.AddScoped<IAuthService, AuthService>();

        // Public self-service onboarding (create your own community + admin). Scoped: it uses the
        // request-scoped IAuthService to log the new admin in after provisioning.
        services.AddScoped<UrbanNest.Application.Onboarding.IOnboardingService, UrbanNest.Infrastructure.Onboarding.OnboardingService>();

        // Resident self-onboarding + admin approval (mobile login/registration flow).
        services.AddScoped<UrbanNest.Application.Onboarding.IResidentOnboardingService, UrbanNest.Infrastructure.Onboarding.ResidentOnboardingService>();

        // Fail-closed default; the request pipeline (Task 2.2) replaces this with a principal-derived
        // context. TryAdd so a host registering the real one first always wins.
        services.TryAddScoped<ITenantContext, NullTenantContext>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            if (configureProvider is not null)
            {
                configureProvider(options);
            }
            else
            {
                var dbOptions = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
                options.UseSqlServer(dbOptions.ConnectionString, sql =>
                    sql.CommandTimeout(dbOptions.CommandTimeoutSeconds));
            }

            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        return services;
    }
}
