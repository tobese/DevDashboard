using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// ── Helpers ──────────────────────────────────────────────────────────

static async Task<string> RunCommand(string command, string arguments)
{
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };

    process.Start();
    var output = await process.StandardOutput.ReadToEndAsync();
    await process.WaitForExitAsync();
    return output;
}

static string Which(string command)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which",
            Arguments = command,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        var result = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        return p.ExitCode == 0 ? result.Split('\n')[0].Trim() : "";
    }
    catch { return ""; }
}

// ── API: Global npm packages ─────────────────────────────────────────

app.MapGet("/api/npm-globals", async () =>
{
    try
    {
        var json = await RunCommand("npm", "list -g --depth=0 --json");
        using var doc = JsonDocument.Parse(json);
        var deps = doc.RootElement.GetProperty("dependencies");
        var packages = new List<object>();
        foreach (var prop in deps.EnumerateObject())
        {
            var version = prop.Value.TryGetProperty("version", out var v) ? v.GetString() : "unknown";
            packages.Add(new { name = prop.Name, version });
        }
        return Results.Ok(new { source = "npm", count = packages.Count, packages });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { source = "npm", error = ex.Message, packages = Array.Empty<object>() });
    }
});

// ── API: Global dotnet tools ─────────────────────────────────────────

app.MapGet("/api/dotnet-tools", async () =>
{
    try
    {
        var output = await RunCommand("dotnet", "tool list -g");
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var packages = new List<object>();
        // Skip header lines (Package Id, Version, Commands + separator)
        foreach (var line in lines.Skip(2))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                packages.Add(new { name = parts[0], version = parts[1], commands = parts.Length > 2 ? parts[2] : "" });
            }
        }
        return Results.Ok(new { source = "dotnet-tools", count = packages.Count, packages });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { source = "dotnet-tools", error = ex.Message, packages = Array.Empty<object>() });
    }
});

// ── API: NuGet global package cache ──────────────────────────────────

app.MapGet("/api/nuget-cache", () =>
{
    try
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var cachePath = Path.Combine(home, ".nuget", "packages");
        var packages = new List<object>();

        if (Directory.Exists(cachePath))
        {
            foreach (var pkgDir in Directory.GetDirectories(cachePath))
            {
                var name = Path.GetFileName(pkgDir);
                var versions = Directory.GetDirectories(pkgDir)
                    .Select(Path.GetFileName)
                    .OrderDescending()
                    .ToArray();
                if (versions.Length > 0)
                {
                    packages.Add(new { name, latestCached = versions[0], versionCount = versions.Length });
                }
            }
        }
        return Results.Ok(new { source = "nuget-cache", path = cachePath, count = packages.Count, packages });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { source = "nuget-cache", error = ex.Message, packages = Array.Empty<object>() });
    }
});

// ── API: System info ─────────────────────────────────────────────────

app.MapGet("/api/system-info", async () =>
{
    string? nodeVersion = null;
    string? npmVersion = null;
    string? dotnetVersion = null;

    try { nodeVersion = (await RunCommand("node", "--version")).Trim(); } catch { }
    try { npmVersion = (await RunCommand("npm", "--version")).Trim(); } catch { }
    try { dotnetVersion = (await RunCommand("dotnet", "--version")).Trim(); } catch { }

    var dotnetSdks = new List<string>();
    try
    {
        var sdkOutput = await RunCommand("dotnet", "--list-sdks");
        dotnetSdks = sdkOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
    }
    catch { }

    return Results.Ok(new
    {
        os = RuntimeInformation.OSDescription,
        arch = RuntimeInformation.OSArchitecture.ToString(),
        machineName = Environment.MachineName,
        userName = Environment.UserName,
        dotnetRuntime = RuntimeInformation.FrameworkDescription,
        nodeVersion,
        npmVersion,
        dotnetSdkVersion = dotnetVersion,
        dotnetSdks,
        npmPath = Which("npm"),
        nodePath = Which("node"),
        dotnetPath = Which("dotnet"),
        timestamp = DateTime.UtcNow
    });
});

app.Run("http://localhost:5555");
