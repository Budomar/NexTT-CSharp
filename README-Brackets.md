# BracketsCalculator (.NET 8 / C# 12)

Source: Budomar/NexTT-test at bd27572536bf67fb340cb3d767d0ba9e9f4179e5,
nextt/core/brackets.py. C# base: Budomar/NexTT-CSharp main downloaded for this task.
Existing files (including Power, Pallet, projects and golden_results.json) are unchanged.
Golden file is byte-identical to bd27572; it was not regenerated.

Added NexTT.Core/BracketsCalculator.cs and NexTT.Tests/BracketsCalculatorTests.cs.
No package/project changes. All five rule tables are literal copies, retaining row order.
No external Python, Excel, files or golden dependency in the Core implementation.
Only tests read the golden file. No TemplateManager implementation.

5987 golden cases: calculate 3278, calculate_for_manual 2707, calculate_total 2.
One inventory Fact checks counts, targets and zero exceptions: 5988 brackets tests.
Full solution: 7871 tests (1883 previous + 5988 brackets).
Linux x64: SDK 8.0.408, runtime 8.0.15, Release; filtered and full runs passed.
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 was set for this container (no ICU).
No invariant-globalization project setting was introduced. NETSDK1188 locale
warnings in the build log are retained. Windows has not been tested here.
Actual command logs and TRX reports: BracketsTestResults/.

Run from solution root:
    dotnet restore NexTT.sln
    dotnet test NexTT.sln --configuration Release --filter FullyQualifiedName~BracketsCalculatorTests
    dotnet test NexTT.sln --configuration Release

Contract: Calculate / CalculateForManual return ordered (Article, Quantity) pairs.
CalculateTotal takes BracketRadiatorItem rows and returns article -> BracketTotal,
whose only JSON field is qty. Missing Connection defaults to VK-правое;
explicit null and unknown connections use VK_K. String matching is ordinal/case-sensitive.
No normalization, sorting, deduplication or zero/negative filtering.
Automatic/manual rule tables remain distinct, including different range gaps.
Quantity is int as agreed; Python arbitrary-precision integers beyond the int
range are outside this typed contract. Diagnostic Python logger calls are not ported.

This archive has no bin/obj caches. Existing README/logs/checksums describe
prior iterations; SHA256SUMS-Brackets.txt lists the files for this deliverable.
No repository push was performed.
