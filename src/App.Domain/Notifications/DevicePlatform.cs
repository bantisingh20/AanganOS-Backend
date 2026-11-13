namespace App.Domain.Notifications;

/// <summary>
/// The client platform a <see cref="Device"/> runs on. Phase 1 ships Android (both the Resident
/// and Guard React Native apps target Android); the other members are modeled so later platforms
/// register devices without a schema change.
/// </summary>
public enum DevicePlatform
{
    Unknown = 0,
    Android = 1,
    Ios = 2,
    Web = 3,
}
