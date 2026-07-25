# Server Project Consolidation Design

## Goal

Collapse the four server-side Clean Architecture projects (`Server.Core`, `Server.BL`,
`Server.Infrastructure`, `Server.WebAPI`) into a single `Server.WebAPI` project.
`Server.TransitDataWorker` is **not touched** — it is an independent host with its own
lifecycle and remains a separate project.

---

## Motivation

The current layering is premature:

| Project | Actual contents |
|---|---|
| `Server.Core` | 1 interface (`IKeyValueRepository<T>`) |
| `Server.BL` | 1 empty placeholder class (`TestService`) |
| `Server.Infrastructure` | 1 class (`InMemoryKeyValueRepository<T>`, ~40 lines) |
| `Server.WebAPI` | All real work: endpoints, SignalR hubs, DI wiring, hosted services |

`Server.WebAPI` already references both `Server.BL` and `Server.Infrastructure` directly,
so the intended dependency inversion is not being enforced anyway. Testability is
unaffected because interfaces remain in the same assembly and a `*.Tests` project can still
reference `Server.WebAPI` directly.

---

## Before / After

### Before

```
src/Server/
  ChefKnifeStudios.MartaJazz.Server.Core/
    Interfaces/
      IKeyValueRepository.cs
  ChefKnifeStudios.MartaJazz.Server.BL/
    Services/
      TestService.cs                  ← empty placeholder, delete
  ChefKnifeStudios.MartaJazz.Server.Infrastructure/
    InMemoryKeyValueRepository.cs
  ChefKnifeStudios.MartaJazz.Server.WebAPI/
    EndpointGroups/
      GtfsEndpoints.cs
      TestEndpoints.cs
    GtfsStatic/
      GtfsStaticLoader.cs
    SignalR/
      TransitHub.cs
      WorkerTransitHub.cs
    Program.cs
  ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/   ← unchanged
```

### After

```
src/Server/
  ChefKnifeStudios.MartaJazz.Server.WebAPI/
    EndpointGroups/
      GtfsEndpoints.cs
      TestEndpoints.cs
    GtfsStatic/
      GtfsStaticLoader.cs
    Interfaces/                        ← moved from Server.Core
      IKeyValueRepository.cs
    Repositories/                      ← moved from Server.Infrastructure
      InMemoryKeyValueRepository.cs
    SignalR/
      TransitHub.cs
      WorkerTransitHub.cs
    Program.cs
  ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/   ← unchanged
```

---

## Step-by-Step Instructions

### 1. Move source files into `Server.WebAPI`

| Source file | Destination inside `Server.WebAPI` |
|---|---|
| `Server.Core/Interfaces/IKeyValueRepository.cs` | `Interfaces/IKeyValueRepository.cs` |
| `Server.Infrastructure/InMemoryKeyValueRepository.cs` | `Repositories/InMemoryKeyValueRepository.cs` |

Create the `Interfaces/` and `Repositories/` folders inside `Server.WebAPI` if they do
not already exist.

### 2. Update namespaces

| File | Old namespace | New namespace |
|---|---|---|
| `IKeyValueRepository.cs` | `ChefKnifeStudios.MartaJazz.Server.Core.Interfaces` | `ChefKnifeStudios.MartaJazz.Server.WebAPI.Interfaces` |
| `InMemoryKeyValueRepository.cs` | `ChefKnifeStudios.MartaJazz.Server.Infrastructure` | `ChefKnifeStudios.MartaJazz.Server.WebAPI.Repositories` |

### 3. Update `using` directives in `Program.cs`

Remove:
```csharp
using ChefKnifeStudios.MartaJazz.Server.Core.Interfaces;
using ChefKnifeStudios.MartaJazz.Server.Infrastructure;
using ChefKnifeStudios.MartaJazz.Server.BL.Services;
```

Add:
```csharp
using ChefKnifeStudios.MartaJazz.Server.WebAPI.Interfaces;
using ChefKnifeStudios.MartaJazz.Server.WebAPI.Repositories;
```

No logic changes required in `Program.cs` — the DI registration line stays identical:
```csharp
builder.Services.AddSingleton(typeof(IKeyValueRepository<>), typeof(InMemoryKeyValueRepository<>));
```

### 4. Update `using` directives in `InMemoryKeyValueRepository.cs`

Remove:
```csharp
using ChefKnifeStudios.MartaJazz.Server.Core.Interfaces;
```

Add:
```csharp
using ChefKnifeStudios.MartaJazz.Server.WebAPI.Interfaces;
```

### 5. Update `Server.WebAPI.csproj`

Remove the project references to the three eliminated projects:
```xml
<ProjectReference Include="..\ChefKnifeStudios.MartaJazz.Server.BL\..." />
<ProjectReference Include="..\ChefKnifeStudios.MartaJazz.Server.Infrastructure\..." />
```

`Server.Core` was never directly referenced by `Server.WebAPI.csproj` (only transitively
via BL), so no explicit removal is needed for it. Verify the final `<ItemGroup>` for
project references contains only `ServiceDefaults`:
```xml
<ItemGroup>
  <ProjectReference Include="..\..\ChefKnifeStudios.MartaJazz.ServiceDefaults\..." />
</ItemGroup>
```

Merge NuGet packages from the removed projects into `Server.WebAPI.csproj`. The
consolidated package list:

```xml
<ItemGroup>
  <PackageReference Include="Ardalis.Result" Version="10.1.0" />
  <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.0" />
  <PackageReference Include="Microsoft.Identity.Web" Version="3.8.2" />
  <PackageReference Include="Scalar.AspNetCore" Version="2.12.41" />
  <PackageReference Include="StackExchange.Redis" Version="2.9.17" />
</ItemGroup>
```

> `StackExchange.Redis` is currently unused but was present in `Server.Infrastructure`.
> Carry it forward so it is available when Redis persistence is wired up.
> `Microsoft.Extensions.Hosting` from `Server.BL` is already provided by the
> `Microsoft.NET.Sdk.Web` SDK — do not add it explicitly.

### 6. Delete the three eliminated projects

Delete these directories entirely:
- `src/Server/ChefKnifeStudios.MartaJazz.Server.Core/`
- `src/Server/ChefKnifeStudios.MartaJazz.Server.BL/`
- `src/Server/ChefKnifeStudios.MartaJazz.Server.Infrastructure/`

### 7. Remove projects from the solution file

Open the `.sln` file at the repository root and remove the three `Project(...)` blocks and
their corresponding `GlobalSection` entries for:
- `ChefKnifeStudios.MartaJazz.Server.Core`
- `ChefKnifeStudios.MartaJazz.Server.BL`
- `ChefKnifeStudios.MartaJazz.Server.Infrastructure`

### 8. Verify the `AppHost` is unaffected

`AppHost.csproj` references only `Server.WebAPI` and `Server.TransitDataWorker` — no
changes needed there.

---

## Files Not Touched

- `Server.TransitDataWorker` — all files unchanged
- `AppHost` — no changes
- `Client.*` projects — no changes
- `Shared` project — no changes
- `ServiceDefaults` — no changes

---

## Verification Checklist

After the consolidation, confirm:

- [ ] `dotnet build` succeeds from the solution root with zero errors
- [ ] `dotnet run --project src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI` starts
      without errors
- [ ] The Aspire AppHost launches both `Server.WebAPI` and `Server.TransitDataWorker`
      successfully
- [ ] `GET /gtfs/routes/shapes` returns data (confirms `IKeyValueRepository` DI is wired)
- [ ] SignalR hubs (`/hubs/transit`, `/hubs/worker-transit`) are reachable
- [ ] No references to the old namespaces (`Server.Core`, `Server.BL`,
      `Server.Infrastructure`) remain anywhere in the solution (use a solution-wide search)
