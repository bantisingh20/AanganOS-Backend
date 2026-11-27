using System.Security.Cryptography;
using App.Application.Auth;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Auth;

/// <summary>
/// Issues OTP codes. A fixed test OTP (Auth:DefaultOtp) is used ONLY when the environment is
/// Development AND Auth:UseDefaultOtp is true. Anywhere else the setting is ignored (with a warning),
/// so a copied config can never turn on a login bypass in production.
/// </summary>
public sealed class OtpGenerator : IOtpGenerator
{
    private readonly string? _fixedOtp;

    public OtpGenerator(IOptions<AuthOptions> options, IHostEnvironment environment, ILogger<OtpGenerator> logger)
    {
        var auth = options.Value;
        var defaultOtp = auth.DefaultOtp?.Trim();
        if (auth.UseDefaultOtp && !string.IsNullOrEmpty(defaultOtp))
        {
            if (environment.IsDevelopment())
            {
                _fixedOtp = defaultOtp;
                logger.LogWarning("Auth:UseDefaultOtp is ON (Development) — every OTP is the fixed test code.");
            }
            else
            {
                logger.LogWarning("Auth:UseDefaultOtp is set in {Environment}; ignored. Fixed OTPs are allowed only in Development.", environment.EnvironmentName);
            }
        }
    }

    public OtpResult Generate(int length) =>
        _fixedOtp is not null
            ? new OtpResult(_fixedOtp, true)
            : new OtpResult(GenerateNumericCode(length), false);

    private static string GenerateNumericCode(int length)
    {
        var digits = length is >= 4 and <= 10 ? length : 6;
        var buffer = new char[digits];
        for (var i = 0; i < digits; i++)
        {
            buffer[i] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
        }

        return new string(buffer);
    }
}
