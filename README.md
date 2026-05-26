# Dev Dashboard

A lightweight, standalone web dashboard that displays your local development environment status — global npm packages, .NET tools, NuGet cache, and system info — all in one place.

Built with ASP.NET Core Minimal API. No Docker, no reverse proxy, no external dependencies beyond the .NET SDK.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or later)

## Quick Start

```bash
git clone https://github.com/<your-username>/DevDashboard.git
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

## Configuration

The server binds to `http://localhost:5555` by default. To change the port, edit `Program.cs`:

```csharp
app.Run("http://localhost:5555"); // change port here
```

## Project Structure

```
DevDashboard/
├── Program.cs                      # API endpoints + startup
├── wwwroot/
│   └── index.html                  # Dashboard UI (vanilla HTML/CSS/JS)
├── Properties/
│   └── launchSettings.json         # Dev launch profile
└── DevDashboard.csproj             # Project file
```

## License

MIT
