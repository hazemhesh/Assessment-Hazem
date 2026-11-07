using LoyaltyPoints.Api.Validation;
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

    [Fact]
    public void RequestValidator_ValidAwardRequest_ReturnsTrue()
    {
        var json = """
        {
            "sourceId": "POS_A",
            "externalTransactionId": "T-1_00",
            "walletId": "W100",
            "points": 25
        }
        """;

        var isValid = RequestValidator.TryParseAndValidateAwardRequest(json, out var request, out var error);

        Assert.True(isValid);
        Assert.Null(error);
        Assert.NotNull(request);
        Assert.Equal("POS_A", request.SourceId);
        Assert.Equal("T-1_00", request.ExternalTransactionId);
        Assert.Equal("W100", request.WalletId);
        Assert.Equal(25, request.Points);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{bad json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("123")]
    [InlineData("\"string\"")]
    [InlineData("""{"sourceId":"POS_A","externalTransactionId":"T1","walletId":"W100"}""")]
    [InlineData("""{"sourceId":123,"externalTransactionId":"T1","walletId":"W100","points":25}""")]
    [InlineData("""{"sourceId":"POS_A","externalTransactionId":"T1","walletId":"W100","points":"25"}""")]
    [InlineData("""{"sourceId":"POS_A","externalTransactionId":"T1","walletId":"W100","points":25.5}""")]
    [InlineData("""{"sourceId":"POS_A","externalTransactionId":"T1","walletId":"W100","points":25.0}""")]
    [InlineData("""{"sourceId":"POS_A","externalTransactionId":"T1","walletId":"W100","points":0}""")]
    [InlineData("""{"sourceId":"POS_A","externalTransactionId":"T1","walletId":"W100","points":-5}""")]
    [InlineData("""{"sourceId":"POS_A","externalTransactionId":"T1","walletId":"W100","points":1001}""")]
    [InlineData("""{"sourceId":"POS A","externalTransactionId":"T1","walletId":"W100","points":25}""")]
    [InlineData("""{"sourceId":"POS_A","externalTransactionId":"T1!","walletId":"W100","points":25}""")]
    [InlineData("""{"sourceId":"POS_A","externalTransactionId":"T1","walletId":"W100_é","points":25}""")]
    public void RequestValidator_InvalidAwardRequest_ReturnsFalseWithExplanation(string rawBody)
    {
        var isValid = RequestValidator.TryParseAndValidateAwardRequest(rawBody, out var request, out var error);

        Assert.False(isValid);
        Assert.Null(request);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void RequestValidator_IdentifierLengthBoundaries_Enforces1To64Chars()
    {
        var id64 = new string('A', 64);
        var id65 = new string('A', 65);

        Assert.True(RequestValidator.TryValidateIdentifier(id64, "walletId", out _));
        Assert.False(RequestValidator.TryValidateIdentifier(id65, "walletId", out var error65));
        Assert.False(string.IsNullOrWhiteSpace(error65));
    }
}
