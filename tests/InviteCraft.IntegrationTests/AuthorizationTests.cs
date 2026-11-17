using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using InviteCraft.Api.Contracts;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Domain.Common;
using InviteCraft.Domain.Entities.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace InviteCraft.IntegrationTests;

/// <summary>Authentication/authorization behaviour that needs no database.</summary>
public class AuthorizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private string TokenFor(params string[] roles)
    {
        var tokens = factory.Services.GetRequiredService<IAccessTokenService>();
        var user = new User { Email = "test@example.com", FullName = "Test User" };
        return tokens.Create(user, roles).Token;
    }

    private async Task<HttpResponseMessage> GetAsync(string url, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Admin_endpoint_without_token_returns_401_envelope()
    {
        var response = await GetAsync("/api/v1/admin/system/whoami");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(TestContext.Current.CancellationToken);
        body!.Success.ShouldBeFalse();
        body.Errors![0].Code.ShouldBe("Auth.Unauthorized");
    }

    [Fact]
    public async Task Customer_cannot_access_admin_endpoints()
    {
        var response = await GetAsync("/api/v1/admin/system/whoami", TokenFor(RoleNames.Customer));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(TestContext.Current.CancellationToken);
        body!.Errors![0].Code.ShouldBe("Auth.Forbidden");
    }

    [Fact]
    public async Task Admin_can_access_admin_endpoints()
    {
        var response = await GetAsync("/api/v1/admin/system/whoami", TokenFor(RoleNames.Admin));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/api/v1/admin/dashboard")]
    [InlineData("/api/v1/admin/reports/sales")]
    [InlineData("/api/v1/admin/reports/sales.csv")]
    [InlineData("/api/v1/admin/customers")]
    [InlineData("/api/v1/admin/orders")]
    [InlineData("/api/v1/admin/payments")]
    [InlineData("/api/v1/admin/coupons")]
    [InlineData("/api/v1/admin/reviews")]
    [InlineData("/api/v1/admin/audit-logs")]
    public async Task Every_phase_11_admin_endpoint_is_closed_to_anonymous_and_customer_callers(string url)
    {
        (await GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync(url, TokenFor(RoleNames.Customer))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/api/v1/notifications")]
    [InlineData("/api/v1/notifications/unread-count")]
    [InlineData("/api/v1/invitations/0193b5a0-0000-7000-8000-000000000000/sharing")]
    public async Task Phase_12_customer_endpoints_require_a_signed_in_user(string url)
    {
        (await GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var token = TokenFor(RoleNames.Customer);
        var tampered = token[..^4] + (token.EndsWith("AAAA") ? "BBBB" : "AAAA");

        var response = await GetAsync("/api/v1/account/profile", tampered);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Resend_verification_requires_authentication()
    {
        var response = await _client.PostAsync("/api/v1/auth/resend-verification", null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Register_with_invalid_input_returns_field_errors()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register",
            new { fullName = "", email = "nope", password = "weak" }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(TestContext.Current.CancellationToken);
        body!.Errors!.Select(e => e.Field).ShouldContain("email");
        body.Errors!.Select(e => e.Field).ShouldContain("password");
    }
}
