using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace InviteCraft.Infrastructure.Payments;

/// <summary>Configuration section "Payments" (shared with <c>CheckoutSettings</c>): which gateway and its credentials. Secrets belong in user-secrets / environment variables, never in the repository.</summary>
public sealed class PaymentSettings
{
    public const string SectionName = "Payments";
    public const string RazorpayProvider = "Razorpay";
    public const string SimulatedProvider = "Simulated";

    /// <summary>"Razorpay" (real) or "Simulated" (development only: an in-process, signed stand-in so the whole flow works without keys).</summary>
    public string Provider { get; set; } = RazorpayProvider;

    /// <summary>Razorpay key id (<c>rzp_test_…</c> / <c>rzp_live_…</c>). Public: it is sent to the browser.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Razorpay key secret. Server-side only; signs and verifies checkout callbacks.</summary>
    public string KeySecret { get; set; } = string.Empty;

    /// <summary>The secret configured on the Razorpay webhook (Dashboard → Webhooks). Signs deliveries.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    public string ApiBaseUrl { get; set; } = "https://api.razorpay.com/v1/";

    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>How often unpaid orders past their deadline are expired.</summary>
    public int ExpirySweepSeconds { get; set; } = 60;
}

/// <summary>Startup validation: a half-configured payment provider must stop the app, not fail on the first customer's payment.</summary>
internal sealed class PaymentSettingsValidator(IHostEnvironment environment) : IValidateOptions<PaymentSettings>
{
    public ValidateOptionsResult Validate(string? name, PaymentSettings settings)
    {
        var errors = new List<string>();

        if (string.Equals(settings.Provider, PaymentSettings.SimulatedProvider, StringComparison.OrdinalIgnoreCase))
        {
            // The simulated gateway accepts payments nobody made. It must never run anywhere customers pay.
            if (!environment.IsDevelopment() && !string.Equals(environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Payments:Provider = \"Simulated\" is only allowed in the Development environment (current: {environment.EnvironmentName}).");
        }
        else if (string.Equals(settings.Provider, PaymentSettings.RazorpayProvider, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(settings.KeyId) || string.IsNullOrWhiteSpace(settings.KeySecret))
                errors.Add("Payments:KeyId and Payments:KeySecret are required for Razorpay. Set them with user-secrets or the Payments__KeyId / Payments__KeySecret environment variables.");
            if (string.IsNullOrWhiteSpace(settings.WebhookSecret))
                errors.Add("Payments:WebhookSecret is required for Razorpay (the secret you set on the webhook in the Razorpay dashboard).");
            if (!Uri.TryCreate(settings.ApiBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                errors.Add("Payments:ApiBaseUrl must be an https URL.");
        }
        else
        {
            errors.Add($"Payments:Provider must be \"Razorpay\" or \"Simulated\" (got \"{settings.Provider}\").");
        }

        if (settings.TimeoutSeconds is < 3 or > 60)
            errors.Add("Payments:TimeoutSeconds must be between 3 and 60.");
        if (settings.ExpirySweepSeconds is < 5 or > 3600)
            errors.Add("Payments:ExpirySweepSeconds must be between 5 and 3600.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

/// <summary>HMAC-SHA256 signatures as Razorpay defines them: lower-case hex, compared in constant time.</summary>
public static class PaymentSignatures
{
    public static string Sign(string secret, string message) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(message))).ToLowerInvariant();

    public static bool IsValid(string secret, string message, string signature)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signature) || signature.Length != 64)
            return false;

        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(secret, message)), Encoding.ASCII.GetBytes(signature.ToLowerInvariant()));
    }

    /// <summary>What Razorpay Checkout signs after a successful payment: <c>order_id|payment_id</c>.</summary>
    public static string CheckoutMessage(string gatewayOrderId, string paymentId) => $"{gatewayOrderId}|{paymentId}";
}
