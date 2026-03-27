namespace Spice86.Tests;

using FluentAssertions;

using System;
using System.Text.Json;

internal static class McpJsonRpcAssertions {
    public static JsonElement GetJsonRpcResult(JsonDocument response) {
        JsonElement root = response.RootElement;
        root.TryGetProperty("error", out JsonElement error).Should().BeFalse($"JSON-RPC error returned: {error}");
        root.TryGetProperty("result", out JsonElement result).Should().BeTrue();
        return result;
    }

    public static JsonElement GetStructuredContent(JsonElement toolResult) {
        if (toolResult.TryGetProperty("structuredContent", out JsonElement structuredContent)) {
            return structuredContent;
        }

        if (toolResult.TryGetProperty("content", out JsonElement content) &&
            content.ValueKind == JsonValueKind.Array &&
            content.GetArrayLength() > 0) {
            JsonElement firstContent = content[0];
            if (firstContent.TryGetProperty("text", out JsonElement text)) {
                string? textValue = text.GetString();
                if (!string.IsNullOrWhiteSpace(textValue)) {
                    JsonDocument parsed = JsonDocument.Parse(textValue);
                    return parsed.RootElement.Clone();
                }
            }
        }

        throw new InvalidOperationException("Expected structuredContent or parseable content text.");
    }

    public static string GetToolErrorMessage(JsonElement toolResult) {
        if (!toolResult.TryGetProperty("content", out JsonElement content) ||
            content.ValueKind != JsonValueKind.Array ||
            content.GetArrayLength() == 0) {
            throw new InvalidOperationException("Expected tool error content array.");
        }

        JsonElement firstContent = content[0];
        if (!firstContent.TryGetProperty("text", out JsonElement text)) {
            throw new InvalidOperationException("Expected tool error text.");
        }

        string? textValue = text.GetString();
        if (textValue == null) {
            throw new InvalidOperationException("Expected non-null tool error message.");
        }

        return textValue;
    }

    public static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value) {
        if (element.TryGetProperty(propertyName, out value)) {
            return true;
        }

        foreach (JsonProperty property in element.EnumerateObject()) {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)) {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
