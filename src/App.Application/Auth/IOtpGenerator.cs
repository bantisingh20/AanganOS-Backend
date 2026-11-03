namespace App.Application.Auth;

public readonly record struct OtpResult(string Code, bool IsDefault);

public interface IOtpGenerator
{
    OtpResult Generate(int length);
}
