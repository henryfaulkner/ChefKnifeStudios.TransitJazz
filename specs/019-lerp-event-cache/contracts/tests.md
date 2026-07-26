# Contract: Test Suite

Pure unit tests only — **integration tests are out of scope for this feature.**

New xUnit project: `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests`.

## Project setup

`.csproj` mirrors `ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <!-- Add a mocking lib (NSubstitute or Moq) only if WorkerTransitHubTests needs IHubContext mocking;
         prefer a hand-written fake if it keeps the dependency out. -->
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\ChefKnifeStudios.TransitJazz.Server.WebAPI\ChefKnifeStudios.TransitJazz.Server.WebAPI.csproj" />
    <ProjectReference Include="..\..\ChefKnifeStudios.TransitJazz.Shared\ChefKnifeStudios.TransitJazz.Shared.csproj" />
  </ItemGroup>
</Project>
```

Add the project to `ChefKnifeStudios.TransitJazz.sln`.

**No host is booted.** Every test runs against plain objects, so `AddHostedService<Worker>()` and `GtfsStaticLoader` never start and the live MARTA GTFS-RT feed is never contacted.

## Test fixtures

- **`FakeLastBatchCache : ILastBatchCache`** — backing field + counters: `WriteCount` and the last value passed to `Set`. Lets `WorkerTransitHubTests` assert write-path wiring (FR-002) without the real cache.
- **Helper** `MakeBatch(params string[] vehicleIds)` → `List<EventEnvelope>` wrapping one `RouteNearestPointBatchEvent` with the given vehicle records, for identity assertions.

## Unit — `LastBatchCacheTests`

| # | Name | Arrange → Act → Assert | FR/INV |
|---|------|------------------------|--------|
| 1 | `New_Current_IsEmptyNonNull` | new `LastBatchCache` → read `Current` → not null, `Count == 0` | FR-004 / INV-1 |
| 2 | `Set_Then_Current_ReturnsSameBatch` | `Set(b1)` → `Current` → same reference / sequence-equal `b1` | FR-002 / INV-2 |
| 3 | `Set_Twice_LatestWins` | `Set(b1)`, `Set(b2)` → `Current == b2` | FR-002 / INV-2 |
| 4 | `Set_Null_YieldsEmptyNonNull` | `Set(null!)` → `Current` not null, empty | INV-1 (defensive) |
| 5 | `Concurrent_SetAndRead_NeverTornOrNull` | spawn N parallel writers (`Set(MakeBatch(...))`) + N readers asserting each read is non-null and is one *whole* known batch (every record belongs to a single `Set` call) | FR-008 / INV-3 |

## Unit — `WorkerTransitHubTests`

`WorkerTransitHub` takes `IHubContext<TransitHub>`, `ILogger<WorkerTransitHub>`, and (new) `ILastBatchCache`. Use a hand-written fake hub context (or NSubstitute) that records `SendAsync` calls.

| # | Name | Asserts | FR |
|---|------|---------|----|
| 1 | `PublishBatch_CachesBatch` | after `PublishBatch(b)`, `FakeLastBatchCache.WriteCount == 1` and last value `== b` | FR-001, FR-002 |
| 2 | `PublishBatch_StillRelays` | `SendAsync("ReceiveBatch", b)` invoked exactly once with the same batch | FR-010 |
| 3 | `PublishBatch_CachesEvenIfEmpty` | `PublishBatch([])` caches `[]` and relays | FR-004 |

## Out of scope for automated tests (covered by quickstart)

Integration tests are not part of this feature. The following are verified manually via the quickstart:

- GET `/transit/last-batch` real HTTP routing + status code, and cold-start `[]` over the wire (quickstart Steps 1–2).
- Real JSON serialization round-trip of polymorphic `EventEnvelope.Payload` over HTTP (quickstart Step 2 body inspection).
- No-upstream-fetch under repeated HTTP reads (quickstart Step 6 corroborates the design; FR-007).
- Client snapshot-fetch-on-load, immediate render, and smooth transition to first live push (quickstart Steps 3–5; FR-005, FR-006).

## Run

```pwsh
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests
# or target this feature's tests across the solution:
dotnet test --filter "FullyQualifiedName~LastBatchCache|FullyQualifiedName~WorkerTransitHub"
```
