using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace IntegrationTests;

public class GatewayRoutingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GatewayRoutingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/api/v1/identity/login")]
    [InlineData("/api/v1/identity/register")]
    [InlineData("/api/v1/cases")]
    [InlineData("/api/v1/documents")]
    [InlineData("/api/v1/audit")]
    [InlineData("/api/v1/search")]
    [InlineData("/api/v1/reports/dashboard")]
    public async Task Gateway_Routes_AreMapped(string path)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        // Assert that the YARP reverse proxy catches the route (doesn't return 404)
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Gateway_UnmappedRoute_Returns_NotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/nonexistent");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
