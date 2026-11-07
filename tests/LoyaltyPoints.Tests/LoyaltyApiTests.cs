using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LoyaltyPoints.Api.Dtos;
using LoyaltyPoints.Api.Validation;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LoyaltyPoints.Tests;

public class LoyaltyApiTests
{
    private static HttpClient CreateIsolatedClient(out WebApplicationFactory<Program> factory)
    {
        factory = new WebApplicationFactory<Program>();
        return factory.CreateClient();
    }

    private static StringContent CreateJsonContent(string rawJson) =>
        new(rawJson, Encoding.UTF8, "application/json");

    private static async Task AssertIsJsonObjectWithExplanationAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(raw));

        using var doc = JsonDocument.Parse(raw);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    // -------------------------------------------------------------------------
    // 1. Initial State & GET /wallets/{walletId}
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("W100", 100)]
    [InlineData("W200", 50)]
    public async Task GetWallet_InitialSeededWallets_Returns200WithBalance(string walletId, int expectedBalance)
    {
        using var client = CreateIsolatedClient(out var factory);
        using var _ = factory;

        var response = await client.GetAsync($"/wallets/{walletId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<WalletResponse>();
        Assert.NotNull(body);
        Assert.Equal(walletId, body.WalletId);
        Assert.Equal(expectedBalance, body.Balance);
    }

    [Theory]
    [InlineData("W999")]
    [InlineData("w100")]
    public async Task GetWallet_UnknownOrWrongCaseWallet_Returns404WithErrorJson(string walletId)
    {
        using var client = CreateIsolatedClient(out var factory);
        using var _ = factory;

        var response = await client.GetAsync($"/wallets/{walletId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.False(string.IsNullOrWhiteSpace(error.Error));
    }

    [Theory]
    [InlineData("W100!")]
    [InlineData("W100_é")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 65 chars
    public async Task GetWallet_InvalidFormatWalletId_Returns400WithErrorJson(string invalidWalletId)
    {
        using var client = CreateIsolatedClient(out var factory);
        using var _ = factory;

        var response = await client.GetAsync($"/wallets/{invalidWalletId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.False(string.IsNullOrWhiteSpace(error.Error));
    }

    // -------------------------------------------------------------------------
    // 2. Validation (POST /awards -> 400 Bad Request)
    // -------------------------------------------------------------------------

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
    public async Task PostAward_InvalidPayloadOrMalformedJson_Returns400AndDoesNotMutateState(string rawBody)
    {
        using var client = CreateIsolatedClient(out var factory);
        using var _ = factory;

        using var content = CreateJsonContent(rawBody);
        var response = await client.PostAsync("/awards", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertIsJsonObjectWithExplanationAsync(response);

        // Verify W100 balance was untouched
        var wallet = await client.GetFromJsonAsync<WalletResponse>("/wallets/W100");
        Assert.Equal(100, wallet!.Balance);
    }

    [Fact]
    public async Task PostAward_BoundaryPoints1And1000And64CharIds_Return201()
    {
        using var client = CreateIsolatedClient(out var factory);
        using var _ = factory;

        var id64 = new string('A', 64);
        var id65 = new string('B', 65);

        Assert.True(RequestValidator.TryValidateIdentifier(id64, "sourceId", out var err64));
        Assert.Null(err64);
        Assert.False(RequestValidator.TryValidateIdentifier(id65, "sourceId", out var err65));
        Assert.NotNull(err65);

        // 1 point + 64-char sourceId and externalTransactionId
        var resMin = await client.PostAsJsonAsync("/awards", new AwardRequest(id64, id64, "W100", 1));
        Assert.Equal(HttpStatusCode.Created, resMin.StatusCode);

        // 1000 points
        var resMax = await client.PostAsJsonAsync("/awards", new AwardRequest("POS_A", "T_MAX", "W100", 1000));
        Assert.Equal(HttpStatusCode.Created, resMax.StatusCode);

        // 65-char identifier -> 400 Bad Request
        var res65 = await client.PostAsJsonAsync("/awards", new AwardRequest(id65, "T_65", "W100", 10));
        Assert.Equal(HttpStatusCode.BadRequest, res65.StatusCode);

        var wallet = await client.GetFromJsonAsync<WalletResponse>("/wallets/W100");
        Assert.Equal(100 + 1 + 1000, wallet!.Balance);
    }

    // -------------------------------------------------------------------------
    // 3. Award Behavior: New Award (201), Matching Retry (200), Identity Rules
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostAward_NewAwardAndMatchingRetry_Returns201Then200WithSameAwardIdAndSingleIncrement()
    {
        using var client = CreateIsolatedClient(out var factory);
        using var _ = factory;

        var request = new AwardRequest("POS_A", "T1", "W100", 25);

        // First call -> 201 Created
        var firstResponse = await client.PostAsJsonAsync("/awards", request);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var createdAward = await firstResponse.Content.ReadFromJsonAsync<AwardResponse>();
        Assert.NotNull(createdAward);
        Assert.False(string.IsNullOrWhiteSpace(createdAward.AwardId));
        Assert.Equal("POS_A", createdAward.SourceId);
        Assert.Equal("T1", createdAward.ExternalTransactionId);
        Assert.Equal("W100", createdAward.WalletId);
        Assert.Equal(25, createdAward.Points);

        // Second call (matching retry) -> 200 OK with identical award record and awardId
        var retryResponse = await client.PostAsJsonAsync("/awards", request);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        var retriedAward = await retryResponse.Content.ReadFromJsonAsync<AwardResponse>();
        Assert.Equal(createdAward, retriedAward);

        // Wallet balance increased only once (100 + 25 = 125)
        var wallet = await client.GetFromJsonAsync<WalletResponse>("/wallets/W100");
        Assert.Equal(125, wallet!.Balance);
    }

    [Fact]
    public async Task PostAward_SameTransactionIdFromDifferentSourcesOrDifferentCasing_AreDistinctAwards()
    {
        using var client = CreateIsolatedClient(out var factory);
        using var _ = factory;

        var reqSourceA = new AwardRequest("POS_A", "T1", "W100", 25);
        var reqSourceB = new AwardRequest("POS_B", "T1", "W100", 25);
        var reqLowerCaseSource = new AwardRequest("pos_a", "T1", "W100", 25);
        var reqLowerCaseTx = new AwardRequest("POS_A", "t1", "W100", 25);

        var res1 = await client.PostAsJsonAsync("/awards", reqSourceA);
        var res2 = await client.PostAsJsonAsync("/awards", reqSourceB);
        var res3 = await client.PostAsJsonAsync("/awards", reqLowerCaseSource);
        var res4 = await client.PostAsJsonAsync("/awards", reqLowerCaseTx);

        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);
        Assert.Equal(HttpStatusCode.Created, res2.StatusCode);
        Assert.Equal(HttpStatusCode.Created, res3.StatusCode);
        Assert.Equal(HttpStatusCode.Created, res4.StatusCode);

        var award1 = await res1.Content.ReadFromJsonAsync<AwardResponse>();
        var award2 = await res2.Content.ReadFromJsonAsync<AwardResponse>();
        Assert.NotEqual(award1!.AwardId, award2!.AwardId);

        var wallet = await client.GetFromJsonAsync<WalletResponse>("/wallets/W100");
        Assert.Equal(100 + (4 * 25), wallet!.Balance);
    }

    // -------------------------------------------------------------------------
    // 4. Conflicting Retry (409), Unknown Wallet (404), and Error Precedence
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostAward_ConflictingRetryWithDifferentPointsOrWallet_Returns409AndPreservesState()
    {
        using var client = CreateIsolatedClient(out var factory);
        using var _ = factory;

        var original = new AwardRequest("POS_A", "T1", "W100", 25);
        var createRes = await client.PostAsJsonAsync("/awards", original);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var originalAward = await createRes.Content.ReadFromJsonAsync<AwardResponse>();

        // Same identity, different points -> 409 Conflict
        var diffPoints = new AwardRequest("POS_A", "T1", "W100", 50);
        var conflictPointsRes = await client.PostAsJsonAsync("/awards", diffPoints);
        Assert.Equal(HttpStatusCode.Conflict, conflictPointsRes.StatusCode);
        await AssertIsJsonObjectWithExplanationAsync(conflictPointsRes);

        // Same identity, different wallet -> 409 Conflict
        var diffWallet = new AwardRequest("POS_A", "T1", "W200", 25);
        var conflictWalletRes = await client.PostAsJsonAsync("/awards", diffWallet);
        Assert.Equal(HttpStatusCode.Conflict, conflictWalletRes.StatusCode);
        await AssertIsJsonObjectWithExplanationAsync(conflictWalletRes);

        // Verify neither W100, W200, nor the original award changed
        var w100 = await client.GetFromJsonAsync<WalletResponse>("/wallets/W100");
        var w200 = await client.GetFromJsonAsync<WalletResponse>("/wallets/W200");
        Assert.Equal(125, w100!.Balance);
        Assert.Equal(50, w200!.Balance);

        var matchingRetry = await client.PostAsJsonAsync("/awards", original);
        Assert.Equal(HttpStatusCode.OK, matchingRetry.StatusCode);
        var retryAward = await matchingRetry.Content.ReadFromJsonAsync<AwardResponse>();
        Assert.Equal(originalAward, retryAward);
    }

    [Fact]
    public async Task PostAward_UnknownWallet_Returns404WithoutConsumingIdentity_AllowingCorrectedRetry()
    {
        using var client = CreateIsolatedClient(out var factory);
        using var _ = factory;

        // 1. Unknown wallet W999 -> 404 Not Found
        var unknownWalletReq = new AwardRequest("POS_A", "T_REUSE", "W999", 30);
        var notFoundRes = await client.PostAsJsonAsync("/awards", unknownWalletReq);
        Assert.Equal(HttpStatusCode.NotFound, notFoundRes.StatusCode);
        await AssertIsJsonObjectWithExplanationAsync(notFoundRes);

        // 2. Corrected request reusing ("POS_A", "T_REUSE") with W200 -> 201 Created
        var correctedReq = new AwardRequest("POS_A", "T_REUSE", "W200", 30);
        var createdRes = await client.PostAsJsonAsync("/awards", correctedReq);
        Assert.Equal(HttpStatusCode.Created, createdRes.StatusCode);

        var w200 = await client.GetFromJsonAsync<WalletResponse>("/wallets/W200");
        Assert.Equal(80, w200!.Balance);
    }

    [Fact]
    public async Task PostAward_OverlappingSemanticError_AcceptedIdentityRetriedWithUnknownWallet_Returns409Before404()
    {
        using var client = CreateIsolatedClient(out var factory);
        using var _ = factory;

        var original = new AwardRequest("POS_A", "T_PREC", "W100", 25);
        var createRes = await client.PostAsJsonAsync("/awards", original);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

        // Retry same identity ("POS_A", "T_PREC") with unknown wallet "W999" -> 409 Conflict takes precedence over 404
        var overlappingReq = new AwardRequest("POS_A", "T_PREC", "W999", 25);
        var conflictRes = await client.PostAsJsonAsync("/awards", overlappingReq);
        Assert.Equal(HttpStatusCode.Conflict, conflictRes.StatusCode);
    }

    // -------------------------------------------------------------------------
    // 5. Concurrency Scenarios (Simultaneous Retries, Distinct Awards, Conflicts)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Concurrency_SimultaneousMatchingRequests_ProducesOne201AndRemaining200WithSingleIncrement()
    {
        using var client = CreateIsolatedClient(out var factory);
        using var _ = factory;

        const int concurrentRequests = 20;
        var request = new AwardRequest("POS_CONC", "T_SAME", "W100", 40);
        using var startGate = new ManualResetEventSlim(false);

        var tasks = Enumerable.Range(0, concurrentRequests).Select(_ => Task.Run(async () =>
        {
            startGate.Wait();
            return await client.PostAsJsonAsync("/awards", request);
        })).ToArray();

        startGate.Set();
        var responses = await Task.WhenAll(tasks);

        var createdCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);

        Assert.Equal(1, createdCount);
        Assert.Equal(concurrentRequests - 1, okCount);

        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<AwardResponse>()));
        var distinctAwardIds = bodies.Select(b => b!.AwardId).Distinct().ToList();
        Assert.Single(distinctAwardIds);

        var wallet = await client.GetFromJsonAsync<WalletResponse>("/wallets/W100");
        Assert.Equal(140, wallet!.Balance);
    }

    [Fact]
    public async Task Concurrency_SimultaneousDistinctAwardsToSameWallet_AllReflectedInBalance()
    {
        using var client = CreateIsolatedClient(out var factory);
        using var _ = factory;

        const int distinctCount = 50;
        const int pointsPerAward = 10;
        using var startGate = new ManualResetEventSlim(false);

        var tasks = Enumerable.Range(1, distinctCount).Select(i => Task.Run(async () =>
        {
            var req = new AwardRequest("POS_CONC", $"T_DISTINCT_{i}", "W100", pointsPerAward);
            startGate.Wait();
            return await client.PostAsJsonAsync("/awards", req);
        })).ToArray();

        startGate.Set();
        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        var wallet = await client.GetFromJsonAsync<WalletResponse>("/wallets/W100");
        Assert.Equal(100 + (distinctCount * pointsPerAward), wallet!.Balance);
    }

    [Fact]
    public async Task Concurrency_SimultaneousConflictingRequestsWithSameIdentity_ProducesOne201AndOne409()
    {
        using var client = CreateIsolatedClient(out var factory);
        using var _ = factory;

        var reqA = new AwardRequest("POS_CONC", "T_CONFLICT_RACE", "W100", 25);
        var reqB = new AwardRequest("POS_CONC", "T_CONFLICT_RACE", "W100", 75);
        using var startGate = new ManualResetEventSlim(false);

        var taskA = Task.Run(async () =>
        {
            startGate.Wait();
            return await client.PostAsJsonAsync("/awards", reqA);
        });

        var taskB = Task.Run(async () =>
        {
            startGate.Wait();
            return await client.PostAsJsonAsync("/awards", reqB);
        });

        startGate.Set();
        var responses = await Task.WhenAll(taskA, taskB);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var winnerResponse = responses.Single(r => r.StatusCode == HttpStatusCode.Created);
        var winnerAward = await winnerResponse.Content.ReadFromJsonAsync<AwardResponse>();

        var wallet = await client.GetFromJsonAsync<WalletResponse>("/wallets/W100");
        Assert.Equal(100 + winnerAward!.Points, wallet!.Balance);
    }
}
