# ADR 0001: Rewrite the project in C# / .NET

## Status

Accepted · 2026-10-09

## Context

CinePosto started as a team class project. The original implementation is a Python scraper, a FastAPI backend with SQLite, and a vanilla JavaScript frontend.

This repository is a from-scratch rewrite with two goals:

- build a complete, production-style application, from requirements to cloud deployment;
- use the stack of the target environment: C#/.NET, Azure, GitHub Actions and Infrastructure as Code.

## Decision

The new implementation uses C# on the current .NET LTS release:

- ASP.NET Core for the API;
- Entity Framework Core for data access;
- C# connectors behind a common interface for the scraper;
- Azure for hosting and Bicep for infrastructure(to be seen).

Hosting details (Functions, App Service, database) are recorded in later ADRs.

## Alternatives considered

| Option | Why not |
|---|---|
| Keep Python | Works, but does not exercise the .NET and Azure tooling this rewrite is meant to cover. |
| Hybrid: Python scraper and .NET API | Two toolchains, two test setups and two build pipelines for a small project. |
| Node.js / TypeScript | Outside the target stack. |

## Consequences

Positive:

- One language and one toolchain end to end.
- Strong typing and first-class Azure and GitHub Actions tooling.

Negative:

- The rewrite takes time, and behavior may drift from the original.
- HTML and JSON parsing libraries have to be learned again.

