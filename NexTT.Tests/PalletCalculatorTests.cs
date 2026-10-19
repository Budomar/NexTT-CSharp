using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using NexTT.Core;
using Xunit;

namespace NexTT.Tests;

public sealed class PalletCalculatorTests
{
    private static readonly JsonElement[] PalletCases = LoadCases();
    private static JsonElement[] LoadCases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "golden_results.json")));
        if (document.RootElement.GetProperty("schema_version").GetInt32() != 1)
            throw new InvalidDataException("Unsupported golden schema.");
        return document.RootElement.GetProperty("cases").EnumerateArray()
            .Where(c => c.GetProperty("id").GetString()!.StartsWith("pallet.", StringComparison.Ordinal))
            .Select(c => c.Clone()).ToArray();
    }

    public static IEnumerable<object[]> GoldenCases() => PalletCases.Select(c => new object[]
    {
        c.GetProperty("id").GetString()!, c.GetRawText()
    });

    [Fact]
    public void BaselineContainsExactly858PalletCases() => Assert.Equal(858, PalletCases.Length);

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public void MatchesPythonGolden(string id, string caseJson)
    {
        using var document = JsonDocument.Parse(caseJson);
        JsonElement c = document.RootElement;
        JsonElement expected = c.GetProperty("expected");
        string target = c.GetProperty("target").GetString()!;
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
        "pallet.get_pallet_info", "pallet.is_approximate", "pallet.get_pallet_weight",
        "pallet_spec", "pallet_totals"
    };

    private static object? Invoke(JsonElement c)
    {
        JsonElement a = c.GetProperty("args");
        switch (c.GetProperty("target").GetString())
        {
            case "pallet.get_pallet_info":
                var (capacity, size) = PalletCalculator.GetPalletInfo(a[0].GetString(), a[1].GetInt32(), a[2].GetInt32());
                return new object?[] { capacity, size };
            case "pallet.is_approximate":
                return PalletCalculator.IsApproximate(a[0].GetString(), a[1].GetInt32(), a[2].GetInt32());
            case "pallet.get_pallet_weight":
                return PalletCalculator.GetPalletWeight(a[0].GetString());
            case "pallet_spec":
                var provider = new FakeWeightProvider(a[1]);
                var calculator = new PalletCalculator(provider);
                var input = a[0].EnumerateArray().Select(row => new RadiatorRow
                {
                    Артикул = row.TryGetProperty("Артикул", out var art) ? art.GetString()! : "",
                    Наименование = row.TryGetProperty("Наименование", out var name) ? name.GetString()! : "",
                    Кол_во = row.TryGetProperty("Кол-во", out var qty) ? Input(qty) : 0
                });
                var rows = calculator.CalculateForSpec(input);
                return new Dictionary<string, object?>
                {
                    ["columns"] = rows.Count == 0 ? Array.Empty<string>() : Columns,
                    ["rows"] = rows.Select(RowMap).ToList(),
                    ["totals"] = TotalsMap(calculator.CalculateTotals(rows))
                };
            case "pallet_totals":
                var totalsInput = a[0].EnumerateArray().Select(row => new CalculatedRow
                {
                    Длина_мм = row.TryGetProperty("Длина, мм", out var l) ? Input(l) : "",
                    Паллет_шт = row.TryGetProperty("Паллет, шт", out var p) ? Input(p) : 0,
                    Вес_с_паллетами_кг = row.TryGetProperty("Вес с паллетами, кг", out var w) ? Input(w) : 0
                });
                return TotalsMap(new PalletCalculator().CalculateTotals(totalsInput));
            default: throw new NotSupportedException(c.GetProperty("target").GetString());
        }
    }

    private static readonly string[] Columns =
    [
        "№", "Артикул", "Наименование", "Кол-во", "Длина, мм", "На паллете, шт",
        "Паллет, шт", "Вес 1, кг", "Вес общий, кг", "Вес паллет, кг", "Вес с паллетами, кг"
    ];

    private static Dictionary<string, object?> RowMap(CalculatedRow row) => new()
    {
        ["№"] = row.Номер, ["Артикул"] = row.Артикул,
        ["Наименование"] = row.Наименование, ["Кол-во"] = row.Кол_во,
        ["Длина, мм"] = row.Длина_мм, ["На паллете, шт"] = row.На_паллете_шт,
        ["Паллет, шт"] = row.Паллет_шт, ["Вес 1, кг"] = row.Вес_1_кг,
        ["Вес общий, кг"] = row.Вес_общий_кг, ["Вес паллет, кг"] = row.Вес_паллет_кг,
        ["Вес с паллетами, кг"] = row.Вес_с_паллетами_кг
    };

    private static Dictionary<string, object?> TotalsMap(PalletTotals t) => new()
    {
        ["pallets_short"] = t.PalletsShort, ["pallets_long"] = t.PalletsLong,
        ["floor_meters"] = t.FloorMeters, ["trucks"] = t.Trucks,
        ["total_weight_kg"] = t.TotalWeightKg, ["weight_per_truck_kg"] = t.WeightPerTruckKg
    };

    // Adapt the raw Python provider fixture to the typed IWeightProvider contract.
    // Missing or nonnumeric weights both produce zero in the source calculator.
    private sealed class FakeWeightProvider(JsonElement weights) : IWeightProvider
    {
        public double? FindWeight(string article)
        {
            if (!weights.TryGetProperty(article, out var value)) return null;
            if (value.ValueKind == JsonValueKind.Number) return value.GetDouble();
            if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)) return parsed;
            return null;
        }
    }

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
