using Microsoft.AspNetCore.Mvc.Testing;

namespace LoyaltyPoints.Tests;

public class LoyaltyApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LoyaltyApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Host_StartsSuccessfully()
    {
        using var client = _factory.CreateClient();
        Assert.NotNull(client);
    }
}
