using App.Application.Querying;
using App.Contracts.Envelope;

namespace App.Application.Residents;

public interface IResidentService
{
    
    Task<ResidentDto> CreateAsync(CreateResidentRequest request, CancellationToken cancellationToken = default);

    
    Task<ResidentDto> UpdateAsync(Guid residentId, UpdateResidentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Soft-delete (archive) a resident (Req 15.5). The row is retained and still retrievable with <c>includeArchived</c>.</summary>
    Task DeleteAsync(Guid residentId, CancellationToken cancellationToken = default);

    /// <summary>Fetch a single resident, tenant-scoped. When <paramref name="includeArchived"/> is true a soft-deleted resident is returned (Req 15.5).</summary>
    Task<ResidentDto?> GetAsync(Guid residentId, bool includeArchived = false, CancellationToken cancellationToken = default);

   
    Task<PagedData<ResidentDto>> ListAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default);

    
    Task<HouseholdMemberDto> AssociateHouseholdAsync(Guid residentId, AssociateHouseholdRequest request, CancellationToken cancellationToken = default);

    /// <summary>Add an emergency contact to a resident (Req 15.3). Unknown resident → not found.</summary>
    Task<EmergencyContactDto> AddEmergencyContactAsync(Guid residentId, AddEmergencyContactRequest request, CancellationToken cancellationToken = default);

    /// <summary>List a resident's emergency contacts (Req 15.3), tenant-scoped. Unknown resident → not found.</summary>
    Task<IReadOnlyList<EmergencyContactDto>> ListEmergencyContactsAsync(Guid residentId, CancellationToken cancellationToken = default);

    
    Task<IReadOnlyList<UnitHouseholdMemberDto>> GetUnitHouseholdAsync(Guid unitId, CancellationToken cancellationToken = default);

    
    Task<IReadOnlyList<ResidentHouseholdUnitDto>> GetResidentHouseholdAsync(Guid residentId, CancellationToken cancellationToken = default);

    
    Task<ResidentMeDto> GetMeAsync(CancellationToken cancellationToken = default);

    
    Task<ResidentDto> SubmitMyKycAsync(SubmitKycRequest request, CancellationToken cancellationToken = default);

    
    Task<ResidentLoginResult> CreateLoginAsync(Guid residentId, CreateResidentLoginRequest request, CancellationToken cancellationToken = default);

    
    Task<ResidentDto> ApproveAsync(Guid residentId, CancellationToken cancellationToken = default);

   
    Task<ResidentDto> RejectAsync(Guid residentId, RejectResidentRequest request, CancellationToken cancellationToken = default);

    
    Task<ResidentDto> ResubmitForVerificationAsync(Guid residentId, CancellationToken cancellationToken = default);

    
    Task<HouseholdMemberDto> MoveInAsync(Guid residentId, MoveInRequest request, CancellationToken cancellationToken = default);

    
    Task<HouseholdMemberDto> MoveOutAsync(Guid residentId, MoveOutRequest request, CancellationToken cancellationToken = default);
}
