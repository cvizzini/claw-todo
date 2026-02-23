using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace TodoApp.Api.Tests;

public class HealthTests : TestBase
{
    public HealthTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Health_ReturnsOkWithHealthyStatus()
    {
        var resp = await Client.GetAsync("/health");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        // Health check framework returns "Healthy" (enum ToString), not "healthy"
        body.GetProperty("status").GetString().Should().Be("Healthy");
        body.TryGetProperty("totalDuration", out _).Should().BeTrue();
    }

    [Fact]
    public async Task HealthReady_ReturnsOkAndIncludesDatabaseCheck()
    {
        var resp = await Client.GetAsync("/health/ready");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Healthy");

        var checks = body.GetProperty("checks").EnumerateArray().ToList();
        checks.Should().Contain(c => c.GetProperty("name").GetString() == "database");
    }

    [Fact]
    public async Task HealthLive_ReturnsAlive()
    {
        var resp = await Client.GetAsync("/health/live");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("alive");
    }
}
