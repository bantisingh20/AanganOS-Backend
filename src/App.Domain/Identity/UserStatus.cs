using System.Text.Json.Serialization;

namespace App.Domain.Identity;

/// <summary>
/// Lifecycle state of a <see cref="User"/> principal. Independent of soft-delete: an
/// <see cref="Active"/> user can log in; a <see cref="Suspended"/> user is retained but denied
/// access by Auth_Service (Task 5.2). Soft-delete (archival) is a separate concern on the base
/// entity (Req 3.7).
/// </summary>
/// <remarks>
/// Serialized across the API as its string NAME ("Active"/"Suspended"), not its ordinal — the
/// mobile clients send and read the name (see usersClient.ts). A type-local converter (same
/// pattern as <c>ErrorCode</c>) keeps this contract without flipping global enum serialization,
/// so every other enum keeps its existing numeric wire form.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<UserStatus>))]
public enum UserStatus
{
    /// <summary>Normal, able to authenticate.</summary>
    Active = 0,

    /// <summary>Temporarily disabled by an administrator; cannot authenticate.</summary>
    Suspended = 1,
}
