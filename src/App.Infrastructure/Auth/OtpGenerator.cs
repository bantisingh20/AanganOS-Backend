using System.Security.Cryptography;
using App.Application.Auth;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Auth;

public sealed class OtpGenerator(IOptions<AuthOptions> options, IHostEnvironment environment) : IOtpGenerator
{
    private readonly AuthOptions _auth = options.Value;
    private readonly bool _isDevelopment = environment.IsDevelopment();

    public OtpResult Generate(int length)
    {
        var defaultOtp = _auth.DefaultOtp?.Trim();

        if (!string.IsNullOrEmpty(defaultOtp) && (_isDevelopment || _auth.UseDefaultOtp))
        {
            return new OtpResult(defaultOtp, true);
        }

        return new OtpResult(GenerateNumericCode(length), false);
    }

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
