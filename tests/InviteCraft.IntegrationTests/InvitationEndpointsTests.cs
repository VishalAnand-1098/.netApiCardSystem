using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using InviteCraft.Api.Contracts;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Domain.Common;
using InviteCraft.Domain.Entities.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace InviteCraft.IntegrationTests;

/// <summary>Authentication and request-shape behaviour of the invitation endpoints (no database needed).</summary>
public class InvitationEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string CustomerToken() => factory.Services.GetRequiredService<IAccessTokenService>()
        .Create(new User { Email = "c@example.com", FullName = "Customer" }, [RoleNames.Customer]).Token;

    [Theory]
    [InlineData("GET", "/api/v1/invitations")]
    [InlineData("POST", "/api/v1/invitations")]
    [InlineData("GET", "/api/v1/invitations/0193a1b2-0000-7000-8000-000000000001")]
    [InlineData("PUT", "/api/v1/invitations/0193a1b2-0000-7000-8000-000000000001/content")]
    [InlineData("POST", "/api/v1/invitations/0193a1b2-0000-7000-8000-000000000001/images")]
    [InlineData("DELETE", "/api/v1/invitations/0193a1b2-0000-7000-8000-000000000001")]
    public async Task Invitation_endpoints_require_sign_in(string method, string url)
    {
        var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_without_a_template_returns_a_field_error_before_touching_the_database()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/invitations") { Content = JsonContent.Create(new { templateSlug = "" }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CustomerToken());

        var response = await _client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(Ct);
        body!.Errors!.Select(e => e.Field).ShouldContain("templateSlug");
    }

    [Fact]
    public async Task Save_with_a_malformed_field_key_is_rejected_by_validation()
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/invitations/0193a1b2-0000-7000-8000-000000000001/content")
        {
            Content = JsonContent.Create(new { version = 1, values = new[] { new { fieldKey = "Bad Key!", value = "x" } } }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CustomerToken());

        var response = await _client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
