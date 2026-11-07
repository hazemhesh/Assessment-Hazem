using System.Text.Json;
using LoyaltyPoints.Api.Dtos;

namespace LoyaltyPoints.Api.Validation;

public static class RequestValidator
{
    private const int MinIdentifierLength = 1;
    private const int MaxIdentifierLength = 64;
    private const int MinPoints = 1;
    private const int MaxPoints = 1000;

    /// <summary>
    /// Validates a single identifier (e.g., walletId in GET /wallets/{walletId} or POST /awards fields).
    /// Must be 1-64 ASCII letters, digits, underscores, or hyphens.
    /// </summary>
    public static bool TryValidateIdentifier(string? value, string fieldName, out string? errorMessage)
    {
        if (string.IsNullOrEmpty(value))
        {
            errorMessage = $"'{fieldName}' is required and must be a non-empty string.";
            return false;
        }

        if (value.Length < MinIdentifierLength ||
            value.Length > MaxIdentifierLength ||
            !value.All(c => IsValidIdentifierChar(c)))
        {
            errorMessage = $"'{fieldName}' must be 1 to 64 characters long and contain only ASCII letters, digits, underscores, or hyphens.";
            return false;
        }

        errorMessage = null;
        return true;
    }

    private static bool IsValidIdentifierChar(char c) =>
        char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-';

    /// <summary>
    /// Validates an AwardRequest from a parsed JsonElement.
    /// Ensures the root is a JSON object, all required fields exist with exact JSON types,
    /// identifiers match the 1-64 ASCII character rule, and points is an integer in 1..1000.
    /// </summary>
    public static bool TryValidateAwardElement(
        JsonElement root,
        out AwardRequest? request,
        out string? errorMessage)
    {
        request = null;

        if (root.ValueKind != JsonValueKind.Object)
        {
            errorMessage = "Request body must be a JSON object.";
            return false;
        }

        if (!TryExtractIdentifierProperty(root, "sourceId", out var sourceId, out errorMessage))
        {
            return false;
        }

        if (!TryExtractIdentifierProperty(root, "externalTransactionId", out var externalTransactionId, out errorMessage))
        {
            return false;
        }

        if (!TryExtractIdentifierProperty(root, "walletId", out var walletId, out errorMessage))
        {
            return false;
        }

        if (!TryExtractPointsProperty(root, out var points, out errorMessage))
        {
            return false;
        }

        request = new AwardRequest(sourceId!, externalTransactionId!, walletId!, points);
        errorMessage = null;
        return true;
    }

    /// <summary>
    /// Helper for parsing raw JSON strings directly in unit tests.
    /// </summary>
    public static bool TryParseAndValidateAwardRequest(
        string? rawBody,
        out AwardRequest? request,
        out string? errorMessage)
    {
        request = null;

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            errorMessage = "Request body is required and must be a valid JSON object.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            return TryValidateAwardElement(doc.RootElement, out request, out errorMessage);
        }
        catch (JsonException)
        {
            errorMessage = "Request body contains malformed JSON.";
            return false;
        }
    }

    private static bool TryExtractIdentifierProperty(
        JsonElement root,
        string propertyName,
        out string? value,
        out string? errorMessage)
    {
        value = null;

        if (!root.TryGetProperty(propertyName, out var prop))
        {
            errorMessage = $"'{propertyName}' is required.";
            return false;
        }

        if (prop.ValueKind != JsonValueKind.String)
        {
            errorMessage = $"'{propertyName}' must be a string.";
            return false;
        }

        var strValue = prop.GetString();
        if (!TryValidateIdentifier(strValue, propertyName, out errorMessage))
        {
            return false;
        }

        value = strValue;
        return true;
    }

    private static bool TryExtractPointsProperty(
        JsonElement root,
        out int points,
        out string? errorMessage)
    {
        points = 0;

        if (!root.TryGetProperty("points", out var prop))
        {
            errorMessage = "'points' is required.";
            return false;
        }

        if (prop.ValueKind != JsonValueKind.Number ||
            !prop.TryGetInt32(out points) ||
            points < MinPoints ||
            points > MaxPoints)
        {
            errorMessage = $"'{nameof(points)}' must be an integer from {MinPoints} to {MaxPoints} (inclusive).";
            return false;
        }

        errorMessage = null;
        return true;
    }
}
