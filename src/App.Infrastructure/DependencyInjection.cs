using App.Application.Abstractions;
using App.Application.Abstractions.Audit;
using App.Application.Abstractions.Caching;
using App.Application.Abstractions.Idempotency;
using App.Application.Abstractions.Security;
using App.Application.Amenities;
using App.Application.Audit;
using App.Application.Auth;
using App.Application.Community;
using App.Application.Config;
using App.Application.Files;
using App.Application.Gate;
using App.Application.Helpdesk;
using App.Application.Identity;
using App.Application.Maintenance;
using App.Application.MaintenanceBilling;
using App.Application.Messaging;
using App.Application.MyTasks;
using App.Application.Notifications;
using App.Application.Onboarding;
using App.Application.Parcel;
using App.Application.Parking;
using App.Application.Querying;
using App.Application.Residents;
using App.Application.Subscription;
using App.Infrastructure.Amenities;
using App.Infrastructure.Audit;
using App.Infrastructure.Auth;
using App.Infrastructure.Caching;
using App.Infrastructure.Community;
using App.Infrastructure.Config;
using App.Infrastructure.Files;
using App.Infrastructure.Gate;
using App.Infrastructure.Helpdesk;
using App.Infrastructure.Idempotency;
using App.Infrastructure.Identity;
using App.Infrastructure.Maintenance;
using App.Infrastructure.MaintenanceBilling;
using App.Infrastructure.Messaging;
using App.Infrastructure.MyTasks;
using App.Infrastructure.Notifications;
using App.Infrastructure.Onboarding;
using App.Infrastructure.Parcel;
using App.Infrastructure.Parking;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Interceptors;
using App.Infrastructure.Querying;
using App.Infrastructure.Residents;
using App.Infrastructure.Security;
using App.Infrastructure.Subscription;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace App.Infrastructure;

/// <summary>
/// The list of every service the app uses ("interface -> class"). Called once by the API and the
/// Worker. To add a feature: create the interface + class, then add ONE line in its section below.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DbContextOptionsBuilder>? configureProvider = null)
    {
        // ---------------------------------------------------------- settings (appsettings.json sections)
        void Settings<T>(string section) where T : class =>
            services.AddOptions<T>().Bind(configuration.GetSection(section)).ValidateDataAnnotations().ValidateOnStart();

        Settings<DatabaseOptions>(DatabaseOptions.SectionName);
        Settings<PaginationOptions>(PaginationOptions.SectionName);
        Settings<CacheOptions>(CacheOptions.SectionName);
        Settings<BulkOptions>(BulkOptions.SectionName);
        Settings<AuthOptions>(AuthOptions.SectionName);
        Settings<JwtSettings>(JwtSettings.SectionName);
        Settings<FileStorageOptions>(FileStorageOptions.SectionName);
        Settings<NotificationOptions>(NotificationOptions.SectionName);
        Settings<NotificationWorkerOptions>(NotificationWorkerOptions.SectionName);
        Settings<MessageOutboxWorkerOptions>(MessageOutboxWorkerOptions.SectionName);
        Settings<IdempotencyOptions>(IdempotencyOptions.SectionName);
        services.AddOptions<SmtpOptions>().Bind(configuration.GetSection(SmtpOptions.SectionName)); // optional, not validated
        services.AddOptions<SmsOptions>().Bind(configuration.GetSection(SmsOptions.SectionName));

        // ---------------------------------------------------------- database + logged-in user
        // Anonymous by default (sees nothing). The API swaps in the token user (AuthSetup.AddJwtLogin);
        // background jobs swap in CurrentUser.System.
        services.TryAddScoped(_ => CurrentUser.Anonymous);
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            if (configureProvider is not null)
            {
                configureProvider(options); // tests plug in SQLite here
            }
            else
            {
                var db = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
                options.UseSqlServer(db.ConnectionString, sql =>
                {
                    sql.CommandTimeout(db.CommandTimeoutSeconds);
                    sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                });
            }

            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        // ---------------------------------------------------------- shared: cache, paging, config, audit
        services.AddMemoryCache();
        var cache = configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>() ?? new CacheOptions();
        if (cache.UseRedis)
        {
            if (string.IsNullOrWhiteSpace(cache.RedisConnectionString))
            {
                throw new InvalidOperationException("Cache:Provider is 'Redis' but Cache:RedisConnectionString is not configured.");
            }

            services.AddStackExchangeRedisCache(redis =>
            {
                redis.Configuration = cache.RedisConnectionString;
                redis.InstanceName = cache.InstanceName;
            });
            services.AddSingleton<IAppCache, RedisAppCache>();
        }
        else
        {
            services.AddSingleton<IAppCache, MemoryAppCache>();
        }

        services.AddSingleton<IAsyncQueryExecutor, EfAsyncQueryExecutor>();
        services.AddScoped<IPageSizePolicy, ConfigPageSizePolicy>();
        services.AddScoped<IConfigService, ConfigService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<App.Application.Diagnostics.IErrorLogService, App.Infrastructure.Diagnostics.ErrorLogService>();
        services.AddScoped<IConfigAuditSink, AuditConfigAuditSink>();
        services.AddScoped<IIdempotencyService, IdempotencyService>();

        // ---------------------------------------------------------- login (token code: Auth/JwtTokens.cs)
        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();
        services.AddSingleton<IOtpGenerator, OtpGenerator>();
        services.AddSingleton<JwtTokenIssuer>();
        services.TryAddScoped<IOtpDeliverer, LoggingOtpDeliverer>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IOnboardingService, OnboardingService>();
        services.AddScoped<IResidentOnboardingService, ResidentOnboardingService>();

        // ---------------------------------------------------------- users, roles, permissions, menu
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IUserRoleService, UserRoleService>();
        services.AddScoped<IPermissionCatalog, PermissionCatalog>();
        services.AddScoped<IEffectivePermissionQuery, EffectivePermissionQuery>();
        services.AddScoped<IMenuService, MenuService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<AuthorizationService>();
        services.AddScoped<IPermissionEvaluator>(sp => sp.GetRequiredService<AuthorizationService>());
        services.AddScoped<IAuthorizationGuard>(sp => sp.GetRequiredService<AuthorizationService>());

        // ---------------------------------------------------------- organizations, communities, units
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<ICommunityService, CommunityService>();
        services.AddScoped<IHierarchyService, HierarchyService>();
        services.AddScoped<IUnitService, UnitService>();
        services.AddScoped<IUnitBulkService, UnitBulkService>();
        services.AddScoped<IUnitHistoryService, UnitHistoryService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<ISubscriptionGate, SubscriptionGate>();

        // ---------------------------------------------------------- residents
        services.AddScoped<IResidentService, ResidentService>();
        services.AddScoped<IResidentAssetsService, ResidentAssetsService>();
        services.AddScoped<ICommunicationPreferenceService, CommunicationPreferenceService>();
        services.AddScoped<ICommunicationPreferences, ResidentCommunicationPreferences>();

        // ---------------------------------------------------------- features
        services.AddScoped<IGateService, GateService>();
        services.TryAddScoped<IAccessControlIntegration, LoggingAccessControlIntegration>();
        services.AddScoped<ISecurityOpsService, SecurityOpsService>();
        services.AddScoped<IParcelService, ParcelService>();
        services.AddScoped<IHelpdeskService, HelpdeskService>();
        services.AddScoped<IMaintenanceService, MaintenanceService>();
        services.AddScoped<IMaintenanceBillingService, MaintenanceBillingService>();
        services.AddScoped<IAmenityService, AmenityService>();
        services.AddScoped<IParkingService, ParkingService>();
        services.AddScoped<IMyTasksService, MyTasksService>();
        services.AddScoped<IAnnouncementService, AnnouncementService>();

        // ---------------------------------------------------------- files
        services.AddSingleton<IImageCompressionService, ImageCompressionService>();
        services.AddSingleton<IFileBlobStore, LocalFileBlobStore>();
        services.AddScoped<IFileService, FileService>();

        // ---------------------------------------------------------- notifications (push) + messages (email/sms)
        services.AddSingleton<INotificationTemplateRenderer, PlaceholderTemplateRenderer>();
        services.AddScoped<INotificationRecipientResolver, DeviceRecipientResolver>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationTemplateAdminService, NotificationTemplateAdminService>();
        services.AddScoped<INotificationInboxService, NotificationInboxService>();
        services.AddScoped<INotificationOutboxQueryService, NotificationOutboxQueryService>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
        services.AddHttpClient<IOneSignalClient, OneSignalHttpClient>((sp, client) =>
        {
            var notif = sp.GetRequiredService<IOptions<NotificationOptions>>().Value;
            client.BaseAddress = new Uri(notif.OneSignalApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(notif.HttpTimeoutSeconds);
        });

        services.AddScoped<IMessageSender, MessageSender>();
        services.AddScoped<IMessageOutboxQueryService, MessageOutboxQueryService>();
        services.AddScoped<IEmailChannelSender, SmtpEmailChannelSender>();
        services.TryAddScoped<ISmsChannelSender, NotConfiguredSmsChannelSender>();
        services.TryAddScoped<IWhatsAppChannelSender, NotConfiguredWhatsAppChannelSender>();
        services.AddScoped<ICommunicationTestService, CommunicationTestService>();

        return services;
    }
}
