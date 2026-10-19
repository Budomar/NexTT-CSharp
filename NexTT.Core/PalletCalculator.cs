using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace NexTT.Core;

public interface IWeightProvider
{
    double? FindWeight(string article);
}

// object? is intentional at the input boundary: Python accepts numeric values,
// numeric strings and malformed values, and handles each differently.
public sealed class RadiatorRow
{
    public string Артикул { get; set; } = "";
    public string Наименование { get; set; } = "";
    [JsonPropertyName("Кол-во")]
    public object? Кол_во { get; set; }
}

public sealed class CalculatedRow
{
    public int Номер { get; set; }
    public string Артикул { get; set; } = "";
    public string Наименование { get; set; } = "";
    public int Кол_во { get; set; }
    // Empty strings and malformed totals inputs must remain representable.
    public object? Длина_мм { get; set; } = "";
    public object? На_паллете_шт { get; set; } = "";
    public object? Паллет_шт { get; set; } = 0.0;
    public double Вес_1_кг { get; set; }
    public double Вес_общий_кг { get; set; }
    public double Вес_паллет_кг { get; set; }
    public object? Вес_с_паллетами_кг { get; set; } = 0.0;
}

public sealed class PalletTotals
{
    public double PalletsShort { get; init; }
    public double PalletsLong { get; init; }
    public int FloorMeters { get; init; }
    public double Trucks { get; init; }
    public double TotalWeightKg { get; init; }
    public double WeightPerTruckKg { get; init; }
}

public sealed class PalletCalculator
{
    public const double PalletWeightKg = 15.0;
    public const double TruckLengthM = 13.0;
    public const double TruckMaxWeightKg = 20000.0;
    private readonly IWeightProvider? weightProvider;
    public PalletCalculator(IWeightProvider? weightProvider = null) => this.weightProvider = weightProvider;

    private sealed record Rule(string Type, int Height, int Min, int Max, int Capacity, string Size);
    // Literal transfer of all PALLET_RULES, in source order. No interpolation.
    private static readonly Rule[] Rules =
    [
        new("10", 300, 400, 2000, 48, "1200*800"),
        new("10", 400, 400, 500, 48, "1200*800"),
        new("10", 400, 600, 2000, 36, "1200*800"),
        new("10", 500, 400, 600, 48, "1200*1000"),
        new("10", 500, 700, 2000, 24, "1200*1000"),
        new("10", 600, 400, 600, 48, "1200*1200"),
        new("10", 600, 700, 2000, 24, "1200*1200"),
        new("10", 900, 400, 600, 24, "1200*1000"),
        new("10", 900, 700, 2000, 12, "1200*1000"),
        new("11", 300, 400, 2000, 48, "1200*800"),
        new("11", 400, 400, 500, 48, "1200*800"),
        new("11", 400, 600, 2000, 36, "1200*800"),
        new("11", 500, 400, 600, 48, "1200*1000"),
        new("11", 500, 700, 2000, 24, "1200*1000"),
        new("11", 600, 400, 600, 48, "1200*800"),
        new("11", 600, 700, 2000, 24, "1200*800"),
        new("11", 900, 400, 600, 24, "1200*1000"),
        new("11", 900, 700, 2000, 12, "1200*1000"),
        new("20", 300, 400, 500, 48, "1200*800"),
        new("20", 300, 600, 2000, 36, "1200*800"),
        new("20", 400, 400, 500, 48, "1200*800"),
        new("20", 400, 600, 2000, 27, "1200*800"),
        new("20", 500, 400, 600, 36, "1200*1000"),
        new("20", 500, 700, 2000, 18, "1200*1000"),
        new("20", 600, 400, 500, 36, "1200*1000"),
        new("20", 600, 600, 2000, 18, "1200*800"),
        new("20", 900, 400, 600, 18, "1200*1000"),
        new("20", 900, 700, 2000, 9, "1200*800"),
        new("21", 300, 400, 2000, 48, "1200*800"),
        new("21", 400, 400, 500, 48, "1200*800"),
        new("21", 400, 600, 2000, 36, "1200*800"),
        new("21", 500, 400, 600, 48, "1200*1000"),
        new("21", 500, 700, 2000, 24, "1200*1000"),
        new("21", 600, 400, 600, 48, "1200*800"),
        new("21", 600, 700, 2000, 24, "1200*800"),
        new("21", 900, 400, 600, 24, "1200*1000"),
        new("21", 900, 700, 2000, 12, "1200*1000"),
        new("22", 300, 400, 500, 48, "1200*800"),
        new("22", 300, 600, 2000, 36, "1200*800"),
        new("22", 400, 400, 500, 48, "1200*800"),
        new("22", 400, 600, 2000, 27, "1200*800"),
        new("22", 500, 400, 600, 36, "1200*1000"),
        new("22", 500, 700, 2000, 18, "1200*1000"),
        new("22", 600, 400, 500, 36, "1200*1000"),
        new("22", 600, 600, 2000, 18, "1200*800"),
        new("22", 900, 400, 600, 18, "1200*1000"),
        new("22", 900, 700, 2000, 9, "1200*800"),
        new("30", 300, 400, 500, 48, "1200*800"),
        new("30", 300, 600, 2000, 24, "1200*800"),
        new("30", 400, 400, 500, 36, "1200*800"),
        new("30", 400, 600, 2000, 18, "1200*800"),
        new("30", 500, 400, 600, 24, "1200*800"),
        new("30", 500, 700, 2000, 12, "1200*800"),
        new("30", 600, 400, 600, 24, "1200*1000"),
        new("30", 600, 700, 2000, 12, "1200*1000"),
        new("30", 900, 400, 600, 12, "1200*1000"),
        new("30", 900, 700, 2000, 6, "1200*800"),
        new("33", 300, 400, 500, 48, "1200*800"),
        new("33", 300, 600, 2000, 24, "1200*800"),
        new("33", 400, 400, 500, 36, "1200*800"),
        new("33", 400, 600, 2000, 18, "1200*800"),
        new("33", 500, 400, 600, 24, "1200*800"),
        new("33", 500, 700, 2000, 12, "1200*1200"),
        new("33", 600, 400, 600, 24, "1200*1000"),
        new("33", 600, 700, 2000, 12, "1200*1200"),
        new("33", 900, 400, 600, 12, "1200*1000"),
        new("33", 900, 700, 2000, 6, "1200*1200"),
        new("10", 300, 2100, 3000, 24, "1200*1200"),
        new("10", 400, 2100, 3000, 24, "1200*1200"),
        new("10", 500, 2100, 3000, 24, "1200*1200"),
        new("10", 600, 2100, 3000, 24, "1200*1200"),
        new("10", 900, 2100, 3000, 24, "1200*1200"),
        new("11", 300, 2100, 3000, 24, "1200*1200"),
        new("11", 400, 2100, 3000, 24, "1200*1200"),
        new("11", 500, 2100, 3000, 24, "1200*1200"),
        new("11", 600, 2100, 3000, 24, "1200*1200"),
        new("11", 900, 2100, 3000, 24, "1200*1200"),
        new("20", 300, 2100, 3000, 18, "1200*1200"),
        new("20", 400, 2100, 3000, 18, "1200*1200"),
        new("20", 500, 2100, 3000, 18, "1200*1200"),
        new("20", 600, 2100, 3000, 18, "1200*1200"),
        new("20", 900, 2100, 3000, 18, "1200*1200"),
        new("21", 300, 2100, 3000, 18, "1200*1200"),
        new("21", 400, 2100, 3000, 18, "1200*1200"),
        new("21", 500, 2100, 3000, 18, "1200*1200"),
        new("21", 600, 2100, 3000, 18, "1200*1200"),
        new("21", 900, 2100, 3000, 18, "1200*1200"),
        new("22", 300, 2100, 3000, 18, "1200*1200"),
        new("22", 400, 2100, 3000, 18, "1200*1200"),
        new("22", 500, 2100, 3000, 18, "1200*1200"),
        new("22", 600, 2100, 3000, 18, "1200*1200"),
        new("22", 900, 2100, 3000, 18, "1200*1200"),
        new("30", 300, 2100, 3000, 12, "1200*1200"),
        new("30", 400, 2100, 3000, 12, "1200*1200"),
        new("30", 500, 2100, 3000, 12, "1200*1200"),
        new("30", 600, 2100, 3000, 12, "1200*1200"),
        new("30", 900, 2100, 3000, 12, "1200*1200"),
        new("33", 300, 2100, 3000, 12, "1200*1200"),
        new("33", 400, 2100, 3000, 12, "1200*1200"),
        new("33", 500, 2100, 3000, 12, "1200*1200"),
        new("33", 600, 2100, 3000, 12, "1200*1200"),
        new("33", 900, 2100, 3000, 12, "1200*1200"),
    ];

    public static (int? OnPallet, string? Size) GetPalletInfo(string? radType, int height, int length)
    {
        string rt = (radType ?? "None").Trim();
        foreach (Rule r in Rules)
            if (r.Type == rt && r.Height == height && r.Min <= length && length <= r.Max)
                return (r.Capacity, r.Size);
        return (null, null);
    }

    public static bool IsApproximate(string? radType, int height, int length) => length > 2000;
    public static double GetPalletWeight(string? size) => PalletWeightKg;

    private static (string? Type, int? Height, int? Length) ExtractParamsFromName(string name)
    {
        string? type = null;
        int? height = null, length = null;
        string[] parts = name.Split('/');
        if (parts.Length >= 3)
        {
            Match lm = Regex.Match(parts[^1], @"(\d{3,4})");
            Match hm = Regex.Match(parts[^2], @"(\d{3,4})");
            if (lm.Success) length = int.Parse(lm.Value, CultureInfo.InvariantCulture);
            if (hm.Success) height = int.Parse(hm.Value, CultureInfo.InvariantCulture);
            for (int i = parts.Length - 3; i >= 0; i--)
            {
                Match tm = Regex.Match(parts[i], @"(\d{2})");
                if (tm.Success) { type = tm.Value; break; }
            }
        }
        if (type is not ("10" or "11" or "20" or "21" or "22" or "30" or "33")) type = null;
        if (height is not (300 or 400 or 500 or 600 or 900)) height = null;
        if (length is null or < 400 or > 3000) length = null;
        return (type, height, length);
    }

    public IReadOnlyList<CalculatedRow> CalculateForSpec(IEnumerable<RadiatorRow> spec)
    {
        var result = new List<CalculatedRow>();
        int index = 0;
        foreach (RadiatorRow row in spec)
        {
            index++;
            string name = row.Наименование;
            if (name.Contains("Кронштейн", StringComparison.Ordinal)) continue;
            int qty;
            try { qty = PythonInt(row.Кол_во); }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or ArgumentNullException)
            { qty = 0; }
            if (qty <= 0) continue;
            string article = row.Артикул.Trim();
            var (type, height, length) = ExtractParamsFromName(name);
            int? onPallet = null;
            string? size = null;
            double count = 0.0;
            if (type is not null && height is not null && length is not null)
            {
                (onPallet, size) = GetPalletInfo(type, height.Value, length.Value);
                if (onPallet is > 0) count = Round2((double)qty / onPallet.Value);
            }
            double weight = weightProvider?.FindWeight(article) ?? 0.0;
            double total = Round2(weight * qty);
            double palletsWeight = Round2(GetPalletWeight(size) * count);
            result.Add(new CalculatedRow
            {
                Номер = index, Артикул = article, Наименование = name, Кол_во = qty,
                Длина_мм = length is null ? "" : (object)length.Value,
                На_паллете_шт = onPallet is null ? "" : (object)onPallet.Value,
                Паллет_шт = count, Вес_1_кг = Round2(weight), Вес_общий_кг = total,
                Вес_паллет_кг = palletsWeight, Вес_с_паллетами_кг = Round2(total + palletsWeight)
            });
        }
        return result;
    }

    public PalletTotals CalculateTotals(IEnumerable<CalculatedRow> calculated)
    {
        double shortCount = 0.0, longCount = 0.0, weight = 0.0;
        foreach (CalculatedRow row in calculated)
        {
            double count = FloatOrZero(row.Паллет_шт);
            if (count > 0)
            {
                int length;
                try { length = PythonInt(row.Длина_мм); }
                catch (Exception ex) when (ex is FormatException or InvalidCastException or ArgumentNullException)
                { length = 0; }
                if (length > 0 && length <= 1200) shortCount += count;
                else if (length > 1200) longCount += count;
            }
            weight += FloatOrZero(row.Вес_с_паллетами_кг);
        }
        int meters = checked((int)(Math.Ceiling(shortCount / 2.0) + Math.Ceiling(longCount)));
        double trucks = meters > 0 ? Round2(meters / TruckLengthM) : 0.0;
        double weightPerTruck = trucks > 0 ? weight / Math.Ceiling(trucks) : 0.0;
        return new PalletTotals
        {
            PalletsShort = Round2(shortCount), PalletsLong = Round2(longCount),
            FloorMeters = meters, Trucks = trucks,
            TotalWeightKg = Round2(weight), WeightPerTruckKg = Round2(weightPerTruck)
        };
    }

    private static double Round2(double value) => Math.Round(value, 2, MidpointRounding.ToEven);

    private static int PythonInt(object? value) => value switch
    {
        null => throw new ArgumentNullException(nameof(value)),
        bool b => b ? 1 : 0,
        string text => ParseInt(text),
        double d when double.IsNaN(d) => throw new FormatException("NaN"),
        double d when double.IsInfinity(d) => throw new OverflowException("Infinity"),
        double d => checked((int)Math.Truncate(d)),
        float f => PythonInt((double)f),
        BigInteger b => checked((int)b),
        byte or sbyte or short or ushort or int or uint or long or ulong =>
            checked((int)BigInteger.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture)),
        _ => throw new InvalidCastException("Expected an integer-compatible value.")
    };

    private static int ParseInt(string text)
    {
        string s = text.Trim();
        if (!Regex.IsMatch(s, @"^[+-]?[0-9](?:_?[0-9])*$")) throw new FormatException("Invalid Python int literal.");
        return checked((int)BigInteger.Parse(s.Replace("_", ""), CultureInfo.InvariantCulture));
    }

    private static double FloatOrZero(object? value)
    {
        if (value is null || value is string { Length: 0 }) return 0.0;
        try
        {
            if (value is string text)
            {
                string s = text.Trim();
                if (s.Equals("nan", StringComparison.OrdinalIgnoreCase)) return double.NaN;
                if (Regex.IsMatch(s, @"^[+\-]?(inf|infinity)$", RegexOptions.IgnoreCase))
                    return s.StartsWith('-') ? double.NegativeInfinity : double.PositiveInfinity;
                if (!Regex.IsMatch(s, @"^[+-]?(?:(?:[0-9](?:_?[0-9])*)(?:\.(?:[0-9](?:_?[0-9])*)?)?|\.[0-9](?:_?[0-9])*)(?:[eE][+-]?[0-9](?:_?[0-9])*)?$"))
                    return 0.0;
                return double.Parse(s.Replace("_", ""), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            if (value is BigInteger b) return (double)b;
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or ArgumentNullException)
        { return 0.0; }
    }
}
