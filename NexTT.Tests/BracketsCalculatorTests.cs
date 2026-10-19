using System.Globalization;
using System.Numerics;
using System.Text.Json;
using NexTT.Core;
using Xunit;

namespace NexTT.Tests;

public sealed class BracketsCalculatorTests
{
    private static readonly Lazy<IReadOnlyDictionary<string, JsonElement>> Cases = new(LoadCases);

    private static IReadOnlyDictionary<string, JsonElement> LoadCases()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "golden_results.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(1, doc.RootElement.GetProperty("schema_version").GetInt32());
        return doc.RootElement.GetProperty("cases").EnumerateArray()
            .Where(c => c.GetProperty("id").GetString()!.StartsWith("brackets.", StringComparison.Ordinal))
            .ToDictionary(c => c.GetProperty("id").GetString()!, c => c.Clone(), StringComparer.Ordinal);
    }

    public static IEnumerable<object[]> GoldenCases() =>
        Cases.Value.Keys.Select(id => new object[] { id });

    [Fact]
    public void Golden_case_inventory_matches_confirmed_baseline()
    {
        var cases = Cases.Value.Values.ToArray();
        Assert.Equal(5987, cases.Length);
        var counts = cases.GroupBy(c => c.GetProperty("target").GetString()!)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Assert.Equal(3, counts.Count);
        Assert.Equal(3278, counts["brackets.calculate"]);
        Assert.Equal(2707, counts["brackets.calculate_for_manual"]);
        Assert.Equal(2, counts["brackets.calculate_total"]);
        Assert.Equal(0, cases.Count(c => c.GetProperty("expected").GetProperty("status").GetString() == "exception"));
        Assert.All(cases, c => Assert.Equal("ok", c.GetProperty("expected").GetProperty("status").GetString()));
    }

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public void Matches_python_golden_result(string id)
    {
        JsonElement c = Cases.Value[id];
        string target = c.GetProperty("target").GetString()!;
        Assert.Contains(target, new[] { "brackets.calculate", "brackets.calculate_for_manual", "brackets.calculate_total" });
        Assert.Empty(c.GetProperty("kwargs").EnumerateObject());
        int arity = c.GetProperty("args").GetArrayLength();
        // Validate routing before the exception branch: adapter mistakes must not pass it.
        if (target == "brackets.calculate_total") Assert.Equal(2, arity);
        else Assert.Contains(arity, new[] { 4, 6 });

        JsonElement expected = c.GetProperty("expected");
        if (expected.GetProperty("status").GetString() == "exception")
        {
            Assert.NotNull(Record.Exception(() => { Invoke(c); }));
            return;
        }
        Assert.Equal("ok", expected.GetProperty("status").GetString());
        JsonElement result = expected.GetProperty("result");
        // This group contains only integers, strings, lists and dictionaries.
        Assert.Empty(result.GetProperty("float64_bits").EnumerateObject());
        JsonElement actual = JsonSerializer.SerializeToElement(Invoke(c));
        Compare(result.GetProperty("value"), actual, id, "$");
    }

    private static object Invoke(JsonElement c)
    {
        JsonElement a = c.GetProperty("args");
        switch (c.GetProperty("target").GetString())
        {
            case "brackets.calculate":
                return Pairs(a.GetArrayLength() == 4
                    ? BracketsCalculator.Calculate(a[0].GetString(), a[1].GetInt32(), a[2].GetInt32(), a[3].GetString())
                    : BracketsCalculator.Calculate(a[0].GetString(), a[1].GetInt32(), a[2].GetInt32(), a[3].GetString(), a[4].GetString(), a[5].GetInt32()));
            case "brackets.calculate_for_manual":
                return Pairs(a.GetArrayLength() == 4
                    ? BracketsCalculator.CalculateForManual(a[0].GetString(), a[1].GetInt32(), a[2].GetInt32(), a[3].GetString())
                    : BracketsCalculator.CalculateForManual(a[0].GetString(), a[1].GetInt32(), a[2].GetInt32(), a[3].GetString(), a[4].GetString(), a[5].GetInt32()));
            case "brackets.calculate_total":
                var items = a[0].EnumerateArray().Select(item => new BracketRadiatorItem
                {
                    RadiatorType = item.GetProperty("rad_type").GetString(),
                    Length = item.GetProperty("length").GetInt32(),
                    Height = item.GetProperty("height").GetInt32(),
                    Quantity = item.GetProperty("qty").GetInt32(),
                    Connection = item.TryGetProperty("connection", out var connection)
                        ? connection.GetString() : "VK-правое"
                }).ToArray();
                return BracketsCalculator.CalculateTotal(items, a[1].GetString());
            default:
                throw new InvalidDataException("Unknown target: " + c.GetProperty("target"));
        }
    }

    private static object?[][] Pairs(IReadOnlyList<(string Article, int Quantity)> pairs) =>
        pairs.Select(pair => new object?[] { pair.Article, pair.Quantity }).ToArray();

    private static void Compare(JsonElement expected, JsonElement actual, string id, string path)
    {
        string label = $"{id} at {path}: expected {expected}, actual {actual}";
        Assert.True(expected.ValueKind == actual.ValueKind, label);
        switch (expected.ValueKind)
        {
            case JsonValueKind.Null:
                return;
            case JsonValueKind.True:
            case JsonValueKind.False:
                Assert.Equal(expected.GetBoolean(), actual.GetBoolean());
                return;
            case JsonValueKind.String:
                Assert.True(string.Equals(expected.GetString(), actual.GetString(), StringComparison.Ordinal), label);
                return;
            case JsonValueKind.Number:
                // BigInteger.Parse rejects decimal/exponent representations: no float coercion.
                Assert.True(BigInteger.Parse(expected.GetRawText(), CultureInfo.InvariantCulture) ==
                    BigInteger.Parse(actual.GetRawText(), CultureInfo.InvariantCulture), label);
                return;
            case JsonValueKind.Array:
                Assert.True(expected.GetArrayLength() == actual.GetArrayLength(), label);
                for (int i = 0; i < expected.GetArrayLength(); i++)
                    Compare(expected[i], actual[i], id, $"{path}/{i}");
                return;
            case JsonValueKind.Object:
                var expectedKeys = expected.EnumerateObject().Select(p => p.Name).OrderBy(k => k, StringComparer.Ordinal).ToArray();
                var actualKeys = actual.EnumerateObject().Select(p => p.Name).OrderBy(k => k, StringComparer.Ordinal).ToArray();
                Assert.Equal(expectedKeys, actualKeys);
                foreach (JsonProperty property in expected.EnumerateObject())
                    Compare(property.Value, actual.GetProperty(property.Name), id, $"{path}/{property.Name}");
                return;
            default:
                throw new InvalidDataException(label);
        }
    }
}
