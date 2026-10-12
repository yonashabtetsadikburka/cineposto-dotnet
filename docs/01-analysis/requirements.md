# Requirements

Status: Draft v0.1 · Last updated: 2026-10-09

## 1. Purpose

CinePosto aggregates cinema showtimes for venues in Umbria (Italy) and exposes them through a web app and a REST API, so that viewers can see in one place what is showing, where and when.

This project is a from-scratch rewrite in C#/.NET of an earlier team project (see Credits in the README).

## 2. Actors

| Actor | Description |
|---|---|
| Viewer | Anyone browsing showtimes through the web app or the API |
| Operator | The person who runs and monitors the system |
| Scraper | System actor: scheduled job that collects data from cinema sources |

## 3. Scope

### 3.1 MVP (milestone M1)

| Connector | Source type | Venues |
|---|---|---|
| Clarici | Structured JSON | Foligno |
| Zenith | Static HTML (weekly table) | Perugia |
| The Space | Public REST API, no browser fallback | Corciano, Terni |

### 3.2 After the MVP

- M2: UCI (Perugia/Corciano area), Cinegatti family (4 venues, one parametric connector), Politeama (Terni), PostModernissimo (Perugia).
- M3: enhancements (map, manual re-scrape, notifications).

### 3.3 Out of scope (for now)

User accounts, ticket purchase or payments, ratings and reviews, price data, native mobile apps.

## 4. Functional requirements

| ID | Requirement | Story | Priority |
|---|---|---|---|
| FR-01 | List the films showing on a given day | US-01 | Must |
| FR-02 | Show a film's showtimes grouped by cinema and date | US-02 | Must |
| FR-03 | Filter by cinema or city | US-03 | Must |
| FR-04 | Select a date within the 8-day window | US-04 | Must |
| FR-05 | Show film details | US-05 | Must |
| FR-06 | Search films by title | US-06 | Should |
| FR-07 | Flag showings in original language (VOST) | US-07 | Should |
| FR-08 | Link to the cinema page to buy tickets | US-08 | Should |
| FR-09 | Show cinema address and map position | US-09 | Could |
| FR-10 | Refresh data automatically every 24 hours | US-10 | Must |
| FR-11 | Record which cinema failed, when and why | US-11 | Must |
| FR-12 | Keep serving last known data when a source is down | US-12 | Should |
| FR-13 | Alert on repeated failures of the same cinema | US-13 | Should |
| FR-14 | Trigger a manual re-scrape | US-14 | Could |

## 5. Non-functional requirements

| ID | Area | Requirement |
|---|---|---|
| NFR-01 | Time zone | All dates and times are computed and shown in `Europe/Rome`, regardless of the server time zone. |
| NFR-02 | Data window | The system holds showings for today plus the next 7 days (8 days, rolling). |
| NFR-03 | Freshness | Data is refreshed at least every 24 hours. |
| NFR-04 | Resilience | A failure in one connector never blocks the others. Failed requests are retried with exponential backoff (3 attempts), followed by one delayed second pass. |
| NFR-05 | Observability | Structured logs, per-cinema error records (phase, timestamp, error type), a health endpoint, and an alert on repeated failures. |
| NFR-06 | Security | No secrets in the repository. Secrets come from environment variables locally and from Key Vault in Azure. HTTPS only. Admin endpoints are protected. All input is validated. |
| NFR-07 | Legal and ethics | Terms of service and `robots.txt` are checked before adding a source. Requests are rate-limited and sent with a User-Agent that identifies the project. Connectors do not circumvent anti-bot protections. Only sources with acceptable terms are used. |
| NFR-08 | Attribution | When TMDb images are used, the footer shows the TMDb attribution notice. TMDb data is used for non-commercial purposes only. No commercial APIs with restrictive terms. |
| NFR-09 | Localization | UI default language is Italian. UI strings live in resource files, never hardcoded. Code, comments, commits and documentation are in English. |
| NFR-10 | Testability | Connectors are tested against saved fixtures without network access. The CI runs build and tests on every pull request. Core logic (title normalization, merge) has unit tests. |
| NFR-11 | Performance | Read endpoints respond in under 500 ms at p95 with the expected data volume (a few hundred showings). |
| NFR-12 | Cost | Stay within the Azure for Students credit and free tiers. Budget alerts at 50%, 75% and 90%. Target steady-state cost: at most 10 USD per month. |
| NFR-13 | Data quality | The same film shown in several cinemas appears once (matched on normalized title). Missing fields are stored as null. Enrichment data never overwrites data from the cinema source. |
| NFR-14 | Reproducibility | Azure infrastructure is described as code (Bicep) and versioned in the repository. |
| NFR-15 | Accessibility | The web UI aims at WCAG 2.1 AA for contrast, keyboard navigation and labels (Should). |

## 6. Data requirements

| Entity | Key information |
|---|---|
| Cinema | slug (unique), name, city, address, region, latitude, longitude, website |
| Film | id, title, normalized title, original title, poster, backdrop, synopsis, genres, director, duration |
| Showing | film, cinema, date, times (list), language, screen, source URL |
| ScrapeRun | start, end, status, counts of films, showings and errors |
| ScrapeError | run, cinema, timestamp, phase, error type, message |

## 7. Constraints and assumptions

- Stack: C#, current .NET LTS, ASP.NET Core, EF Core, Azure, GitHub Actions, Bicep.
- Sources are third-party websites and APIs with no SLA and no stability guarantees.
- A single maintainer, working part-time.
- Showtimes belong to the cinemas: each showing links back to its source.

## 8. Open questions

- How long are past showings kept (if at all)?
- Is UCI included, given that its API is undocumented? Check its terms first.
- Local database: SQLite or a containerized server database? Decided in a later ADR.
- Frontend framework: Blazor or React?
- Is the TMDb backdrop worth keeping, given the attribution and non-commercial constraints?