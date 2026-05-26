# Dev Dashboard

[![CI](https://github.com/tobese/DevDashboard/actions/workflows/ci.yml/badge.svg)](https://github.com/tobese/DevDashboard/actions/workflows/ci.yml)

A lightweight, standalone web dashboard that displays your local development environment status — global npm packages, .NET tools, NuGet cache, and system info — all in one place.

Built with ASP.NET Core Minimal API. No Docker, no reverse proxy, no external dependencies beyond the .NET SDK.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or later)

## Quick Start

```bash
git clone https://github.com/tobese/DevDashboard.git
cd DevDashboard
dotnet run
```

Open **http://localhost:5555** in your browser.

## What It Shows

| Tab | Source | Description |
|-----|--------|-------------|
| **System** | OS, CLIs | OS version, .NET runtime/SDKs, Node.js/npm versions and paths |
| **npm Global** | `npm list -g --json` | All globally installed npm packages with versions |
| **dotnet Tools** | `dotnet tool list -g` | All global .NET tools with versions and commands |
| **NuGet Cache** | `~/.nuget/packages` | All packages in the NuGet global cache with cached version counts |

All tables support **search/filter** and **click-to-sort** columns. Hit the **Refresh** button to re-query everything.

## API Endpoints

| Endpoint | Description |
|----------|-------------|
| `GET /api/system-info` | OS, .NET runtime/SDKs, Node.js/npm versions and paths |
| `GET /api/npm-globals` | Global npm packages with versions |
| `GET /api/dotnet-tools` | Global .NET tools with versions and commands |
| `GET /api/nuget-cache` | Packages in the NuGet global cache |

All endpoints return JSON.

## Configuration

The server binds to `http://localhost:5555` by default. To change the port, edit `Program.cs`:

```csharp
app.Run("http://localhost:5555"); // change port here
```

## Extending

To add a new data source:

1. Add a `MapGet` endpoint in `Program.cs` that shells out to a CLI or scans a directory
2. Add a tab and panel in `wwwroot/index.html`
3. Add a `loadXxx()` function in the `<script>` block to fetch and render

Some ideas: Homebrew (`brew list --json`), Docker (`docker ps --format json`), pip (`pip list --format json`), running ports.

## Project Structure

```
DevDashboard/
├── Program.cs                      # API endpoints + startup
├── wwwroot/
│   └── index.html                  # Dashboard UI (vanilla HTML/CSS/JS)
├── Properties/
│   └── launchSettings.json         # Dev launch profile
├── .github/
│   └── workflows/
│       └── ci.yml                  # Build verification workflow
└── DevDashboard.csproj             # Project file
```

## License

MIT
