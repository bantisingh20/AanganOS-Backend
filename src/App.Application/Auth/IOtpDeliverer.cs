namespace App.Application.Auth;

/// <summary>
/// Delivers a generated one-time code to a user's registered contact (Req 2.1). A seam so
/// Auth_Service does not depend on the Notification_Service (Task 7.1) that will ultimately carry
/// the message — until then a logging/stub implementation satisfies the contract without blocking
/// this task. When Notification_Service lands, its adapter implements this interface instead.
///
/// <para><b>Security.</b> The OTP plaintext is handed to the deliverer so it can reach the user,
/// and the deliverer is the one place it may appear — it MUST route it only to the registered
/// contact and MUST NOT write the code to logs (steering: never log secret values).</para>
/// </summary>
public interface IOtpDeliverer
{
    /// <summary>Deliver <paramref name="code"/> to <paramref name="destination"/> (the user's registered email/phone).</summary>
    Task DeliverAsync(string destination, string code, CancellationToken cancellationToken = default);
}
