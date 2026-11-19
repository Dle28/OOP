# OS Simulation Studio

Educational Windows desktop application for CPU scheduling, process synchronization, Banker resource allocation, and virtual memory.

## Read first

Read [`docs/00-BASELINE.md`](docs/00-BASELINE.md) before changing code. It is the authoritative architecture and behavior specification. The root [`AGENTS.md`](AGENTS.md) enforces the read-first and change-synchronization rules.

## Solution map

| Project | Responsibility |
|---|---|
| `src/OsSim.Domain` | Simulation contracts, CPU/synchronization/Banker/memory rules, sessions, and snapshots. |
| `src/OsSim.Application` | Module catalog and application-facing session creation/use cases. |
| `src/OsSim.Wpf` | WPF views, MVVM view models, playback timer, and composition root. |
| `tests/OsSim.Domain.Tests` | Algorithm, validation, state-transition, and session contract tests. |
| `tests/OsSim.Application.Tests` | Catalog and module wiring tests. |

## Build and run

Requires the .NET 10 SDK and Windows Desktop targeting support.

```powershell
dotnet build OsSim.slnx
dotnet test OsSim.slnx
dotnet run --project src/OsSim.Wpf/OsSim.Wpf.csproj
```

The backend is an in-process modular domain/application layer; no web API, database, or external service is required. The application uses deterministic in-memory scenarios only. It does not start real OS processes or access real virtual memory.

For presentation hierarchy, visual style, and animation behavior, see [`docs/01-UI-UX.md`](docs/01-UI-UX.md).

## Architecture diagrams

PlantUML sources live in [`docs/uml/`](docs/uml/). Update them with the baseline whenever a contract, class relationship, or user flow changes.
