using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace NexTT.Core;

/// <summary>Direct port of nextt/core/power_recalculator.py, revision bd27572.</summary>
public static class PowerRecalculator
{
    public const double ReferenceDelta = 70.0;
    public const double ReferenceEps = 0.001;
    public static IReadOnlyList<int> ValidHeights { get; } = Array.AsReadOnly(new[] { 300, 400, 500, 600, 900 });
    public static IReadOnlyList<int> ValidLengths { get; } = Array.AsReadOnly(Enumerable.Range(4, 27).Select(x => x * 100).ToArray());
    public static IReadOnlyList<string> ValidTypes { get; } = Array.AsReadOnly(new[] { "10", "11", "20", "21", "22", "30", "33" });

    // All five source rows are equal; retain the height dimension explicitly.
    private static readonly IReadOnlyDictionary<int, IReadOnlyDictionary<string, double>> Exponents =
        ValidHeights.ToDictionary(h => h, _ => (IReadOnlyDictionary<string, double>)new Dictionary<string, double>
        {
            ["10"] = 1.14, ["11"] = 1.14, ["20"] = 1.18, ["21"] = 1.16,
            ["22"] = 1.14, ["30"] = 1.19, ["33"] = 1.15
        });

    public static double DeltaTFrom(object? supply, object? ret, object? room) =>
        (PythonFloat(supply) + PythonFloat(ret)) / 2.0 - PythonFloat(room);

    public static bool IsReference(double deltaT) => Math.Abs(deltaT - ReferenceDelta) < ReferenceEps;
    public static bool IsValidDelta(double deltaT) => deltaT > 0.0 && deltaT <= 120.0;

    public static string NormalizeType(object? radType)
    {
        string t = PythonString(radType).Trim();
        return t == "12" ? "21" : t;
    }

    public static int NormalizeHeight(object? heightMm)
    {
        BigInteger h;
        try { h = PythonInt(heightMm); }
        catch (Exception e) when (e is FormatException or InvalidCastException or ArgumentNullException)
        { return 500; }

        // Strictly smaller distance preserves Python min's first item on ties.
        int nearest = ValidHeights[0];
        foreach (int candidate in ValidHeights)
            if (BigInteger.Abs(candidate - h) < BigInteger.Abs(nearest - h))
                nearest = candidate;
        return nearest;
    }

    public static double Factor(object? radType, object? heightMm, double deltaT)
    {
        if (IsReference(deltaT)) return 1.0;
        if (deltaT <= 0.0) return 1.0;
        string rt = NormalizeType(radType);
        int h = NormalizeHeight(heightMm);
        if (!Exponents[h].TryGetValue(rt, out double n)) return 1.0;
        return Math.Pow(deltaT / ReferenceDelta, n);
    }

    public static double RecalcPower(object? qnomW, object? radType, object? heightMm, double deltaT)
    {
        if (IsReference(deltaT)) return PythonFloat(qnomW);
        return PythonFloat(qnomW) * Factor(radType, heightMm, deltaT);
    }

    // Adapter boundary replaces DataProvider.radiators_df; no Excel dependency.
    public static Dictionary<string, List<BigInteger>> FullTableForHeight(
        object? heightMm, double deltaT, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        int h = NormalizeHeight(heightMm);
        var result = new Dictionary<string, List<BigInteger>>();
        if (rows.Count == 0) return result;
        List<IReadOnlyDictionary<string, object?>> filtered;
        try
        {
            if (!rows.Any(r => r.ContainsKey("Наименование"))) return result;
            filtered = rows.Where(r => r.TryGetValue("Наименование", out var name)
                && name is string s && s.Contains($"/{h}/", StringComparison.Ordinal)).ToList();
        }
        catch { return result; }

        bool hasTypeColumn = rows.Any(r => r.ContainsKey("RadiatorType"));
        bool hasPowerColumn = rows.Any(r => r.ContainsKey("Мощность, Вт"));
        foreach (string rt in ValidTypes)
        {
            var row = new List<BigInteger>();
            try
            {
                if (!hasTypeColumn) throw new KeyNotFoundException("RadiatorType");
                int type = int.Parse(rt, CultureInfo.InvariantCulture);
                var powerMap = new Dictionary<int, object?>();
                foreach (var rad in filtered)
                {
                    if (!rad.TryGetValue("RadiatorType", out var actualType) || !NumericEquals(actualType, type)) continue;
                    var name = (string)rad["Наименование"]!;
                    Match match = Regex.Match(name, @"/(\d{3,4})$");
                    if (!match.Success) continue;
                    int length = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    // Missing cells in an existing pandas column become NaN.
                    powerMap[length] = rad.TryGetValue("Мощность, Вт", out var q)
                        ? q : hasPowerColumn ? double.NaN : 0;
                }
                foreach (int length in ValidLengths)
                {
                    if (!powerMap.TryGetValue(length, out var qnom) || qnom is null)
                    { row.Add(BigInteger.Zero); continue; }
                    double q = RecalcPower(qnom, rt, h, deltaT);
                    if (!double.IsFinite(q)) throw new ArithmeticException("Cannot round non-finite power.");
                    row.Add(new BigInteger(Math.Round(q, MidpointRounding.ToEven)));
                }
            }
            catch { row = Enumerable.Repeat(BigInteger.Zero, ValidLengths.Count).ToList(); }
            result[rt] = row;
        }
        return result;
    }

    public static Dictionary<int, Dictionary<string, List<BigInteger>>> FullTableAllHeights(
        double deltaT, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) =>
        ValidHeights.ToDictionary(h => h, h => FullTableForHeight(h, deltaT, rows));

    // Python float(None) and float("") must throw; Convert.ToDouble(null) must NOT be used.
    private static double PythonFloat(object? value) => value switch
    {
        null => throw new ArgumentNullException(nameof(value)),
        bool b => b ? 1.0 : 0.0,
        string s => ParseFloat(s),
        BigInteger i => (double)i,
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double =>
            Convert.ToDouble(value, CultureInfo.InvariantCulture),
        _ => throw new InvalidCastException("Expected a Python-compatible numeric value.")
    };

    private static double ParseFloat(string text)
    {
        string s = text.Trim();
        if (s.Equals("inf", StringComparison.OrdinalIgnoreCase) || s.Equals("+inf", StringComparison.OrdinalIgnoreCase)
            || s.Equals("infinity", StringComparison.OrdinalIgnoreCase) || s.Equals("+infinity", StringComparison.OrdinalIgnoreCase))
            return double.PositiveInfinity;
        if (s.Equals("-inf", StringComparison.OrdinalIgnoreCase) || s.Equals("-infinity", StringComparison.OrdinalIgnoreCase))
            return double.NegativeInfinity;
        if (s.Equals("nan", StringComparison.OrdinalIgnoreCase) || s.Equals("+nan", StringComparison.OrdinalIgnoreCase)
            || s.Equals("-nan", StringComparison.OrdinalIgnoreCase)) return double.NaN;
        if (!Regex.IsMatch(s, @"^[+-]?(?:(?:[0-9](?:_?[0-9])*)(?:\.(?:[0-9](?:_?[0-9])*)?)?|\.[0-9](?:_?[0-9])*)(?:[eE][+-]?[0-9](?:_?[0-9])*)?$"))
            throw new FormatException("Invalid Python float literal.");
        return double.Parse(s.Replace("_", ""), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static BigInteger PythonInt(object? value) => value switch
    {
        null => throw new ArgumentNullException(nameof(value)),
        bool b => b ? BigInteger.One : BigInteger.Zero,
        BigInteger i => i,
        string s => ParseInt(s),
        double d when double.IsNaN(d) => throw new FormatException("NaN"),
        double d when double.IsInfinity(d) => throw new OverflowException("Infinity"),
        double d => new BigInteger(d),
        float f => PythonInt((double)f),
        byte or sbyte or short or ushort or int or uint or long or ulong =>
            BigInteger.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture),
        _ => throw new InvalidCastException("Expected an integer-compatible value.")
    };

    private static BigInteger ParseInt(string text)
    {
        string s = text.Trim();
        if (!Regex.IsMatch(s, @"^[+-]?[0-9](?:_?[0-9])*$")) throw new FormatException("Invalid Python int literal.");
        return BigInteger.Parse(s.Replace("_", ""), CultureInfo.InvariantCulture);
    }

    private static string PythonString(object? value) => value switch
    {
        null => "None", bool b => b ? "True" : "False", string s => s,
        double d when double.IsNaN(d) => "nan",
        double d when double.IsPositiveInfinity(d) => "inf",
        double d when double.IsNegativeInfinity(d) => "-inf",
        double d => FloatString(d), float f => FloatString(f),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()!
    };

    private static string FloatString(double d)
    {
        string s = d.ToString("R", CultureInfo.InvariantCulture).ToLowerInvariant();
        return !s.Contains('.') && !s.Contains('e') ? s + ".0" : s;
    }

    private static bool NumericEquals(object? value, int expected) => value switch
    {
        null or string => false,
        _ => PythonFloat(value) == expected
    };
}
