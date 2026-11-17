using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using InviteCraft.Api.Contracts;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Domain.Common;
using InviteCraft.Domain.Entities.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace InviteCraft.IntegrationTests;

/// <summary>Authorization and validation of category endpoints (no database needed).</summary>
public class CategoryEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpRequestMessage Request(HttpMethod method, string url, string? role = null, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (role is not null)
        {
            var token = factory.Services.GetRequiredService<IAccessTokenService>()
                .Create(new User { Email = "t@example.com", FullName = "Test" }, [role]).Token;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return request;
    }

    [Theory]
    [InlineData("GET", "/api/v1/admin/categories")]
    [InlineData("POST", "/api/v1/admin/categories")]
    [InlineData("DELETE", "/api/v1/admin/categories/0193a1b2-0000-7000-8000-000000000001")]
    [InlineData("GET", "/api/v1/admin/subcategories")]
    [InlineData("PATCH", "/api/v1/admin/subcategories/0193a1b2-0000-7000-8000-000000000001/status")]
    [InlineData("GET", "/api/v1/admin/templates")]
    [InlineData("POST", "/api/v1/admin/templates")]
    [InlineData("PUT", "/api/v1/admin/templates/0193a1b2-0000-7000-8000-000000000001/design")]
    [InlineData("POST", "/api/v1/admin/templates/0193a1b2-0000-7000-8000-000000000001/assets")]
    [InlineData("GET", "/api/v1/admin/fonts")]
    [InlineData("DELETE", "/api/v1/admin/fonts/0193a1b2-0000-7000-8000-000000000001")]
    public async Task Admin_category_endpoints_reject_anonymous_and_customers(string method, string url)
    {
        var anonymous = await _client.SendAsync(Request(new HttpMethod(method), url), Ct);
        var customer = await _client.SendAsync(Request(new HttpMethod(method), url, RoleNames.Customer), Ct);

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        customer.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_category_with_invalid_payload_returns_field_errors()
    {
        var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/admin/categories", RoleNames.Admin,
            new { name = "", slug = "Bad Slug", kind = "Festival", icon = "nope", sortOrder = -5 }), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(Ct);
        body!.Errors!.Select(e => e.Field).ShouldBe(["name", "slug", "icon", "sortOrder"], ignoreOrder: true);
    }

    [Fact]
    public async Task Unknown_enum_value_is_a_400_not_a_500()
    {
        var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/admin/categories", RoleNames.Admin,
            new { name = "Onam", kind = "NotAKind", sortOrder = 1 }), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
