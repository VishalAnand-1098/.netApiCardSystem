using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using InviteCraft.Api.Contracts;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Domain.Common;
using InviteCraft.Domain.Entities.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace InviteCraft.IntegrationTests.Rendering;

/// <summary>Authentication, routing and request-shape behaviour of the download endpoints (no database, no browser needed).</summary>
public class DownloadEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string SomeId = "0193a1b2-0000-7000-8000-000000000001";

    private string CustomerToken() => factory.Services.GetRequiredService<IAccessTokenService>()
        .Create(new User { Email = "c@example.com", FullName = "Customer" }, [RoleNames.Customer]).Token;

    [Theory]
    [InlineData("GET", $"/api/v1/invitations/{SomeId}/downloads/options")]
    [InlineData("POST", $"/api/v1/invitations/{SomeId}/downloads")]
    [InlineData("GET", "/api/v1/downloads")]
    [InlineData("GET", $"/api/v1/downloads/{SomeId}")]
    public async Task Download_endpoints_require_sign_in(string method, string url)
    {
        var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("exp=1&sig=AAAA")]                            // expired
    [InlineData("exp=99999999999&sig=AAAA")]                  // far future but forged
    [InlineData("exp=99999999999&sig=")]                      // no signature
    public async Task The_file_endpoint_needs_no_login_but_rejects_forged_or_expired_links_before_touching_the_database(string query)
    {
        var response = await _client.GetAsync($"/api/v1/downloads/{SomeId}/file?{query}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(Ct);
        body!.Errors!.Single().Code.ShouldBe("Download.LinkInvalid");
        response.Headers.CacheControl?.NoStore.ShouldBe(true);
    }

    [Fact]
    public async Task An_unknown_format_is_a_validation_error_not_a_server_error()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/invitations/{SomeId}/downloads")
        {
            Content = JsonContent.Create(new { format = "Gif" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CustomerToken());

        var response = await _client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_resolution_outside_the_sane_range_is_rejected_by_validation()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/invitations/{SomeId}/downloads")
        {
            Content = JsonContent.Create(new { format = "Png", dpi = 50000 }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CustomerToken());

        var response = await _client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(Ct);
        body!.Errors!.Select(e => e.Field).ShouldContain("dpi");
    }

    [Fact]
    public async Task The_admin_thumbnail_endpoint_is_admin_only()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/templates/{SomeId}/thumbnail/generate");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CustomerToken());

        (await _client.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var anonymous = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/templates/{SomeId}/thumbnail/generate");
        (await _client.SendAsync(anonymous, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
