using System.ComponentModel.DataAnnotations;

namespace App.Application.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    [Range(1, 100)]
    public int LockoutThreshold { get; init; } = 5;

    [Range(1, 1440)]
    public int LockoutWindowMinutes { get; init; } = 15;

    [Range(1, 60)]
    public int OtpExpiryMinutes { get; init; } = 10;

    [Range(4, 10)]
    public int OtpLength { get; init; } = 6;

    [MaxLength(10)]
    public string DefaultOtp { get; init; } = "123456";

    public bool UseDefaultOtp { get; init; }

    [Range(1, 43200)]
    public int TokenLifetimeMinutes { get; init; } = 60;

    [Range(1, 365)]
    public int RefreshTokenLifetimeDays { get; init; } = 30;
}
