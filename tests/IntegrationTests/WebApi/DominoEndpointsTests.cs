using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IntegrationTests.WebApi;

public sealed class DominoEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public DominoEndpointsTests(WebApplicationFactory<Program> factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task GetStart_ShouldNotBeSupported()
    {
        HttpResponseMessage response = await client.GetAsync("/start");

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }
}