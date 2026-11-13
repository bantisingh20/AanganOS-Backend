namespace App.Application.Config;


public interface IConfigService
{

    Task<string?> GetAsync(string key, Guid? communityId = null, CancellationToken cancellationToken = default);

    Task<string> GetStringAsync(string key, string defaultValue, Guid? communityId = null, CancellationToken cancellationToken = default);

    Task<int> GetIntAsync(string key, int defaultValue, Guid? communityId = null, CancellationToken cancellationToken = default);

    Task<bool> GetBoolAsync(string key, bool defaultValue, Guid? communityId = null, CancellationToken cancellationToken = default);

    Task<decimal> GetDecimalAsync(string key, decimal defaultValue, Guid? communityId = null, CancellationToken cancellationToken = default);

    Task<TEnum> GetEnumAsync<TEnum>(string key, TEnum defaultValue, Guid? communityId = null, CancellationToken cancellationToken = default)
        where TEnum : struct, Enum;


    Task SetAsync(string key, string value, Guid? communityId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConfigRow>> ListConfigAsync(string? search = null, CancellationToken cancellationToken = default);

    Task<ConfigRow> UpsertConfigAsync(UpsertConfigRequest request, CancellationToken cancellationToken = default);

    Task DeleteConfigAsync(Guid id, CancellationToken cancellationToken = default);


    Task<IReadOnlyList<MasterDataEntry>> GetMasterDataAsync(string listKey, Guid? communityId = null, bool includeInactive = false, CancellationToken cancellationToken = default);


    Task SetMasterDataAsync(string listKey, MasterDataEntry entry, Guid? communityId = null, CancellationToken cancellationToken = default);
}
