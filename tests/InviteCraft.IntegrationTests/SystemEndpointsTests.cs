using System.Net;
using System.Net.Http.Json;
using InviteCraft.Api.Contracts;
using InviteCraft.Api.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace InviteCraft.IntegrationTests;

public class SystemEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task System_info_returns_success_envelope()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/api/v1/system/info", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<SystemInfoDto>>(ct);
        body.ShouldNotBeNull();
        body.Success.ShouldBeTrue();
        body.Data!.Name.ShouldBe("InviteCraft API");
        body.TraceId.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Liveness_probe_is_healthy_without_database()
    {
        var response = await _client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unknown_route_returns_404()
    {
        var response = await _client.GetAsync("/api/v1/does-not-exist", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
