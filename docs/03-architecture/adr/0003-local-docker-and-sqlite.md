# ADR 0003: Run locally with Docker Compose and SQLite

## Status

Accepted · 2026-11-25

## Context

Nothing should be deployed to Azure before the whole system has been run and tested on a developer machine. At the time of this decision the Api, the Scraper and a persistence layer did not exist, and the requirements left one question open: "Local database: SQLite or a containerized server database? Decided in a later ADR" (`docs/01-analysis/requirements.md`).

## Decision

- The Api and the Scraper are built as container images and started with Docker Compose. The Api runs as a service. The Scraper is a one-shot job under the `jobs` profile: `docker compose run --rm scraper`.
- For local runs the database is a SQLite file in a named volume shared by the two containers. It is created and migrated automatically at startup, with Entity Framework Core migrations.
- The Domain does not know about SQLite. It defines `IScrapeResultWriter` and `IShowtimeReader`; the Infrastructure project implements them. Changing the database later means changing Infrastructure only.
- The Scraper has two modes, chosen explicitly with `SCRAPER_MODE` (there is no default): `fixtures` replaces the network with saved responses through an `HttpMessageHandler`, so the connectors run unchanged; `live` uses the real sites. Local runs and CI use `fixtures`.
- The web frontend (`web/`, plain HTML, CSS and JavaScript) is served by the Api container from `wwwroot`, on the same origin as the API, so there is no CORS and no extra service. It only calls the public API. All its texts live in `web/i18n/it.json` (default) and `en.json`.
- Configuration comes from environment variables. The admin token has no default value: the Api refuses to start without a usable one. No secret is stored in the images or in the repository.
- `scripts/smoke.sh` and `scripts/smoke.ps1` run the whole setup and check it. CI runs the smoke test on every pull request.

This decision is about local runs. **The production database is not decided here.**

## Alternatives considered

| Option | Why not |
|---|---|
| A containerized server database (PostgreSQL or SQL Server) in Compose | Closer to a managed Azure database, but it needs another image, another provider package and a wait-for-database step, for a data set of a few hundred rows. It can replace SQLite when the production database is chosen. |
| In-memory database | Nothing survives a restart, and the Api and the Scraper could not share data. |
| Mount the host folder instead of a named volume | Behaves differently on Windows, macOS and Linux, and breaks SQLite locking on some of them. |

## Consequences

Positive:

- One command per step, the same on every machine and in CI.
- The scraper can be tested end to end without the network.
- No database server to install.

Negative:

- The SQLite provider is not the production provider. EF Core migrations are provider specific, so they will probably have to be generated again when the production database is chosen.
- A SQLite file shared by two containers works on a single Docker host only. It must not be put on a network file share, such as Azure Files, because file locking is not reliable there.
- `fixtures` mode covers one synthetic cinema. It proves the wiring, not the behavior of the real sites.
