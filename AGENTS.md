# Project instructions

Before changing project files, read [`docs/00-BASELINE.md`](docs/00-BASELINE.md). It is the canonical record for scope, architecture, class ownership, public contracts, simulation rules, and acceptance criteria.
Before changing views, styles, or presentation animations, also read [`docs/01-UI-UX.md`](docs/01-UI-UX.md).

## Change rules

- Search the baseline and existing source for an owned class or contract before adding another one.
- Keep domain rules in `OsSim.Domain`; do not add simulation logic to WPF views or code-behind.
- Preserve the dependency direction `Wpf -> Application -> Domain`. `Domain` must not reference UI, persistence, or platform APIs.
- When a public contract, algorithm rule, class owner, or module boundary changes, update the baseline and affected PlantUML diagrams in the same change.
- Keep API status accurate: `Planned`, `Implemented`, or `Deprecated`. A design entry is not evidence that code exists.
- Prefer composition and focused interfaces. Do not add a second implementation of an existing responsibility without updating the baseline first.
- User input errors must be returned as typed simulation errors and must not partially mutate a session.
- Keep simulations deterministic and virtual. Do not create operating-system processes or threads to represent simulated actors.
