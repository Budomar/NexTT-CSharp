using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using NexTT.Core;
using Xunit;

namespace NexTT.Tests;

public sealed class PowerRecalculatorTests
{
    // Keep a clone: JsonDocument is disposed before xUnit runs the cases.
    private static readonly JsonElement[] PowerCases = LoadCases();
    private static JsonElement[] LoadCases()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "golden_results.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.GetProperty("schema_version").GetInt32() != 1)
            throw new InvalidDataException("Unsupported golden schema.");
        return document.RootElement.GetProperty("cases").EnumerateArray()
            .Where(c => c.GetProperty("id").GetString()!.StartsWith("power.", StringComparison.Ordinal))
            .Select(c => c.Clone()).ToArray();
    }

    public static IEnumerable<object[]> GoldenCases() => PowerCases.Select(c => new object[]
    {
        c.GetProperty("id").GetString()!, c.GetRawText()
    });

    [Fact]
    public void BaselineContainsAll1023PowerCasesAndThreeExpectedExceptions()
    {
        Assert.Equal(1023, PowerCases.Length);
        Assert.Equal(3, PowerCases.Count(c => c.GetProperty("expected").GetProperty("status").GetString() == "exception"));
    }

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public void MatchesPythonGolden(string id, string caseJson)
    {
        using var document = JsonDocument.Parse(caseJson);
        JsonElement c = document.RootElement;
        JsonElement expected = c.GetProperty("expected");
        string target = c.GetProperty("target").GetString()!;
        // Dispatch/schema errors must never accidentally satisfy an exception case.
        Assert.True(KnownTargets.Contains(target), $"{id}: unsupported target {target}");
        Assert.Empty(c.GetProperty("kwargs").EnumerateObject());
        if (expected.GetProperty("status").GetString() == "exception")
        {
            Assert.NotNull(Record.Exception(() => Invoke(c)));
            return;
        }
        Assert.Equal("ok", expected.GetProperty("status").GetString());
        object? actual = Invoke(c);
        JsonElement result = expected.GetProperty("result");
        var bits = result.GetProperty("float64_bits").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString()!);
        var visited = new HashSet<string>();
        Compare(result.GetProperty("value"), actual, "$", bits, visited, id);
        Assert.Equal(bits.Keys.OrderBy(x => x), visited.OrderBy(x => x));
    }

    private static readonly HashSet<string> KnownTargets = new(StringComparer.Ordinal)
    {
        "power.delta_t_from", "power.is_reference", "power.is_valid_delta",
        "power.normalize_type", "power.normalize_height", "power.factor", "power.recalc_power",
        "power_table", "power_table_probe"
    };

    private static object? Invoke(JsonElement c)
    {
        JsonElement a = c.GetProperty("args");
        object? V(int i) => Input(a[i]);
        double D(int i) => a[i].GetDouble();
        return c.GetProperty("target").GetString() switch
        {
            "power.delta_t_from" => PowerRecalculator.DeltaTFrom(V(0), V(1), V(2)),
            "power.is_reference" => PowerRecalculator.IsReference(D(0)),
            "power.is_valid_delta" => PowerRecalculator.IsValidDelta(D(0)),
            "power.normalize_type" => PowerRecalculator.NormalizeType(V(0)),
            "power.normalize_height" => PowerRecalculator.NormalizeHeight(V(0)),
            "power.factor" => PowerRecalculator.Factor(V(0), V(1), D(2)),
            "power.recalc_power" => PowerRecalculator.RecalcPower(V(0), V(1), V(2), D(3)),
            "power_table" => PowerRecalculator.FullTableForHeight(V(1), D(2), Rows(a[0])),
            "power_table_probe" => PowerRecalculator.FullTableForHeight(V(1), D(2), Rows(a[0]))["22"].Take(3).ToList(),
            _ => throw new NotSupportedException(c.GetProperty("target").GetString())
        };
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows(JsonElement rows) =>
        rows.EnumerateArray().Select(row => (IReadOnlyDictionary<string, object?>)
            row.EnumerateObject().ToDictionary(p => p.Name, p => Input(p.Value))).ToList();

    private static object? Input(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => e.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when e.GetRawText().IndexOfAny(new[] { '.', 'e', 'E' }) >= 0 => (object)e.GetDouble(),
        JsonValueKind.Number => BigInteger.Parse(e.GetRawText(), CultureInfo.InvariantCulture),
        _ => throw new InvalidDataException($"Unsupported input: {e}")
    };

    private static void Compare(JsonElement expected, object? actual, string path,
        IReadOnlyDictionary<string, string> bits, HashSet<string> visited, string id)
    {
        string label = $"{id} at {path}";
        if (bits.TryGetValue(path, out string? expectedBits))
        {
            double d = Assert.IsType<double>(actual);
            string actualBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(d)).ToString("x16", CultureInfo.InvariantCulture);
            Assert.True(expectedBits == actualBits,
                $"{label}: expected float64 0x{expectedBits}, got 0x{actualBits} ({d:R})");
            visited.Add(path);
            return;
        }
        switch (expected.ValueKind)
        {
            case JsonValueKind.Null:
                Assert.Null(actual); break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                Assert.Equal(expected.GetBoolean(), Assert.IsType<bool>(actual)); break;
            case JsonValueKind.String:
                Assert.Equal(expected.GetString(), Assert.IsType<string>(actual)); break;
            case JsonValueKind.Number:
                Assert.True(actual is BigInteger or int or long, $"{label}: expected integer, got {actual?.GetType()}");
                Assert.Equal(BigInteger.Parse(expected.GetRawText(), CultureInfo.InvariantCulture),
                    BigInteger.Parse(Convert.ToString(actual, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture));
                break;
            case JsonValueKind.Array:
                var sequence = Assert.IsAssignableFrom<IEnumerable>(actual).Cast<object?>().ToArray();
                Assert.Equal(expected.GetArrayLength(), sequence.Length);
                for (int i = 0; i < sequence.Length; i++) Compare(expected[i], sequence[i], $"{path}/{i}", bits, visited, id);
                break;
            case JsonValueKind.Object:
                var map = Assert.IsAssignableFrom<IDictionary>(actual);
                string[] keys = map.Keys.Cast<object>().Select(k => Convert.ToString(k, CultureInfo.InvariantCulture)!).ToArray();
                Assert.Equal(expected.EnumerateObject().Select(p => p.Name).OrderBy(k => k), keys.OrderBy(k => k));
                foreach (JsonProperty p in expected.EnumerateObject())
                    Compare(p.Value, map[p.Name], $"{path}/{p.Name}", bits, visited, id);
                break;
            default: throw new InvalidDataException(label);
        }
    }
}
