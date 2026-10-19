# NexTT — PalletCalculator migration (.NET 8 / C# 12)

## Integration into the confirmed Power solution
Copy these two new files to your existing solution:
- NexTT.Core/PalletCalculator.cs
- NexTT.Tests/PalletCalculatorTests.cs
No project changes or new packages are required. The full solution is also included.
PowerRecalculator.cs and PowerRecalculatorTests.cs are unchanged.
The public repository retrieved for this work contained the Python baseline, not the C# solution;
the C# base is the unchanged Power deliverable from the preceding iteration.

## Validation
858 pallet golden cases + one count Fact = 859 pallet tests.
1024 existing power tests + 859 pallet tests = 1883 tests total.
Both filtered and full Release runs pass on Linux x64, SDK 8.0.408 / runtime 8.0.15.
This container has no ICU, so DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 was set for
these runs only. No project setting enables invariant globalization. Windows has
not been run for the pallet implementation. The initial build log includes
NETSDK1188 locale warnings caused by the container's invariant globalization.
Final test logs and TRX reports are included without omitting test failures.

Commands (from the solution root):
    dotnet restore NexTT.sln
    dotnet test NexTT.sln --configuration Release --filter FullyQualifiedName~PalletCalculatorTests
    dotnet test NexTT.sln --configuration Release

## Contract
IWeightProvider.FindWeight(string article) -> double?; null means no known weight.
PalletCalculator.CalculateForSpec(IEnumerable<RadiatorRow>) -> IReadOnlyList<CalculatedRow>.
PalletCalculator.CalculateTotals(IEnumerable<CalculatedRow>) -> PalletTotals.
The interface and POCOs are defined in PalletCalculator.cs, namespace NexTT.Core.
All financial/logistics arithmetic here uses double, never decimal.
Raw quantity/totals properties are object? to retain numeric/string/malformed
inputs used by the existing Python golden fixtures. C# uses Кол_во rather than
Кол-во (a hyphen is not allowed in a C# identifier); the input property has
JsonPropertyName("Кол-во"). Tests explicitly adapt the other Python column names.

## Preserved behaviour
All pallet rules are literal copies in original order; no interpolation or type aliasing.
Length 2001..2099 has no rule. Approximation is length > 2000 independent of rules.
Pallet weight is always 15.0, even with no matched size.
Round(qty / capacity, 2), all intermediate weight rounding, and trucks rounding
use Math.Round(value, 2, MidpointRounding.ToEven). The 858 golden cases match
exact float64 bits; this does not prove equivalence for arbitrary untested inputs
(Python and .NET decimal-place rounding can differ on some binary boundary values).
Names containing the exact case-sensitive substring Кронштейн are skipped.
Quantity uses Python-style truncation for numeric values (1.9 -> 1);
nonintegral strings such as "1.5" are rejected. Invalid/nonpositive quantities skip rows.
Row numbers use the original input index, retaining gaps after skipped rows.
Missing weights are 0.0, independently of pallet calculation.
Invalid raw provider weights are adapted to null in the fake typed provider.
Unknown product names with positive quantity are retained with empty dimensions/capacity.
Rows at length 1200 are short; 1201 are long.
Meters = ceil(short/2) + ceil(long), computed BEFORE rounded totals are returned.
Trucks = round(meters/13.0, 2); weight per truck divides unrounded total weight
by ceil(rounded trucks), then rounds to two decimal places.

## Tests / baseline
The original golden_results.json is copied byte-for-byte and is not regenerated.
Tests filter by id prefix pallet., validate the known target and empty kwargs,
and recursively compare exact values/types, list ordering, keys and float64 bits.
The count Fact asserts exactly 858 cases; it makes no exception-count assertion.
The exception-status branch remains supported for future golden cases.
No BracketsCalculator or TemplateManager implementation has been added.
