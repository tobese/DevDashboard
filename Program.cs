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
    // Read both streams concurrently to avoid deadlock when stderr buffer fills
    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();
    await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync());
    var stdout = await stdoutTask;
    // Fall back to stderr so tools that write version info there (e.g. swift) still work
    return !string.IsNullOrWhiteSpace(stdout) ? stdout : await stderrTask;
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

// ── Helper: run command with explicit argument list (avoids shell-escaping issues) ──

static async Task<string> RunCommandArgs(string command, params string[] args)
{
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };
    foreach (var arg in args)
        process.StartInfo.ArgumentList.Add(arg);
    process.Start();
    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();
    await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync());
    var stdout = await stdoutTask;
    return !string.IsNullOrWhiteSpace(stdout) ? stdout : await stderrTask;
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

// ── API: Java info ──────────────────────────────────────────────────

app.MapGet("/api/java-info", async () =>
{
    string? javaVersion = null;
    string? javacVersion = null;
    string? mavenVersion = null;
    string? gradleVersion = null;
    string? javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");

    try { javaVersion = (await RunCommand("java", "--version")).Split('\n')[0].Trim(); } catch { }
    try { javacVersion = (await RunCommand("javac", "--version")).Trim(); } catch { }
    try { mavenVersion = (await RunCommand("mvn", "--version")).Split('\n')[0].Trim(); } catch { }
    try
    {
        var gOutput = await RunCommand("gradle", "--version");
        gradleVersion = gOutput.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("Gradle "))?.Trim();
    }
    catch { }

    var jvms = new List<string>();
    var jvmRoot = "/Library/Java/JavaVirtualMachines";
    if (Directory.Exists(jvmRoot))
    {
        jvms = [.. Directory.GetDirectories(jvmRoot)
            .Select(Path.GetFileName)
            .Where(n => n != null)
            .Select(n => n!)
            .OrderDescending()];
    }

    return Results.Ok(new
    {
        javaVersion,
        javacVersion,
        mavenVersion,
        gradleVersion,
        javaHome = javaHome ?? "not set",
        javaPath = Which("java"),
        javacPath = Which("javac"),
        jvms,
        timestamp = DateTime.UtcNow
    });
});

// ── API: Android info ─────────────────────────────────────────────────

app.MapGet("/api/android-info", async () =>
{
    var androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME")
                   ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");

    string? adbVersion = null;
    try { adbVersion = (await RunCommand("adb", "version")).Split('\n')[0].Trim(); } catch { }

    var platforms = new List<string>();
    var buildTools = new List<string>();
    var avds = new List<string>();

    if (!string.IsNullOrEmpty(androidHome))
    {
        var platformsPath = Path.Combine(androidHome, "platforms");
        if (Directory.Exists(platformsPath))
        {
            platforms = [.. Directory.GetDirectories(platformsPath)
                .Select(Path.GetFileName)
                .Where(n => n != null)
                .Select(n => n!)
                .OrderDescending()];
        }

        var buildToolsPath = Path.Combine(androidHome, "build-tools");
        if (Directory.Exists(buildToolsPath))
        {
            buildTools = [.. Directory.GetDirectories(buildToolsPath)
                .Select(Path.GetFileName)
                .Where(n => n != null)
                .Select(n => n!)
                .OrderDescending()];
        }
    }

    try
    {
        var avdOutput = await RunCommand("emulator", "-list-avds");
        avds = [.. avdOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))];
    }
    catch { }

    return Results.Ok(new
    {
        androidHome = androidHome ?? "not set",
        adbVersion,
        adbPath = Which("adb"),
        platformCount = platforms.Count,
        buildToolsCount = buildTools.Count,
        avdCount = avds.Count,
        platforms,
        buildTools,
        avds,
        timestamp = DateTime.UtcNow
    });
});

// ── API: iOS info ────────────────────────────────────────────────────

app.MapGet("/api/ios-info", async () =>
{
    string? xcodeVersion = null;
    string? xcodePath = null;
    string? swiftVersion = null;
    string? cocoaPodsVersion = null;
    string? fastlaneVersion = null;

    try { xcodeVersion = (await RunCommand("xcodebuild", "-version")).Split('\n')[0].Trim(); } catch { }
    try { xcodePath = (await RunCommand("xcode-select", "-p")).Trim(); } catch { }
    try { swiftVersion = (await RunCommand("swift", "--version")).Split('\n')[0].Trim(); } catch { }
    try { cocoaPodsVersion = (await RunCommand("pod", "--version")).Trim(); } catch { }
    try
    {
        var flOutput = await RunCommand("fastlane", "--version");
        fastlaneVersion = flOutput.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("fastlane ") && l.Length > 9 && char.IsDigit(l[9]));
    }
    catch { }

    var sdks = new List<object>();
    try
    {
        var sdkOutput = await RunCommand("xcodebuild", "-showsdks");
        foreach (var line in sdkOutput.Split('\n'))
        {
            if (!line.Contains("-sdk")) continue;
            var parts = line.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                sdks.Add(new { name = parts[0].Trim(), sdk = parts[1].Replace("-sdk", "").Trim() });
        }
    }
    catch { }

    var simulators = new List<object>();
    try
    {
        var simJson = await RunCommand("xcrun", "simctl list devices available --json");
        using var simDoc = JsonDocument.Parse(simJson);
        foreach (var runtime in simDoc.RootElement.GetProperty("devices").EnumerateObject())
        {
            if (!runtime.Name.Contains(".iOS-")) continue;
            var runtimeLabel = "iOS " + runtime.Name.Split(".iOS-").Last().Replace("-", ".");
            foreach (var device in runtime.Value.EnumerateArray())
            {
                var name = device.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var state = device.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "";
                simulators.Add(new { name, runtime = runtimeLabel, state });
            }
        }
    }
    catch { }

    return Results.Ok(new
    {
        xcodeVersion,
        xcodePath,
        swiftVersion,
        cocoaPodsVersion,
        fastlaneVersion,
        sdkCount = sdks.Count,
        simulatorCount = simulators.Count,
        sdks,
        simulators,
        timestamp = DateTime.UtcNow
    });
});

// ── API: Docker info ──────────────────────────────────────────────────

app.MapGet("/api/docker-info", async () =>
{
    string? dockerVersion = null;
    string? composeVersion = null;
    string? currentContext = null;

    try { dockerVersion = (await RunCommand("docker", "--version")).Trim(); } catch { }
    try { composeVersion = (await RunCommand("docker", "compose version")).Split('\n')[0].Trim(); } catch { }
    try { currentContext = (await RunCommand("docker", "context show")).Trim(); } catch { }

    var containers = new List<object>();
    var runningCount = 0;
    try
    {
        var psOutput = await RunCommandArgs("docker", "ps", "-a", "--format", "{{json .}}");
        foreach (var line in psOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var name   = root.TryGetProperty("Names",  out var n)   ? n.GetString()   : null;
                var image  = root.TryGetProperty("Image",  out var img) ? img.GetString() : null;
                var state  = root.TryGetProperty("State",  out var st)  ? st.GetString()  : null;
                var status = root.TryGetProperty("Status", out var sts) ? sts.GetString() : null;
                var ports  = root.TryGetProperty("Ports",  out var p)   ? p.GetString()   : null;
                if (state == "running") runningCount++;
                containers.Add(new { name, image, state, status, ports });
            }
            catch { }
        }
    }
    catch { }

    var images = new List<object>();
    try
    {
        var imgOutput = await RunCommandArgs("docker", "images", "--format", "{{json .}}");
        foreach (var line in imgOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var repo    = root.TryGetProperty("Repository",   out var r) ? r.GetString() : null;
                var tag     = root.TryGetProperty("Tag",          out var t) ? t.GetString() : null;
                var size    = root.TryGetProperty("Size",         out var s) ? s.GetString() : null;
                var created = root.TryGetProperty("CreatedSince", out var c) ? c.GetString() : null;
                images.Add(new { repository = repo, tag, size, created });
            }
            catch { }
        }
    }
    catch { }

    return Results.Ok(new
    {
        dockerVersion,
        composeVersion,
        currentContext,
        dockerPath = Which("docker"),
        containerCount = containers.Count,
        runningCount,
        imageCount = images.Count,
        containers,
        images,
        timestamp = DateTime.UtcNow
    });
});

// ── API: Kubernetes info ──────────────────────────────────────────────

app.MapGet("/api/k8s-info", async () =>
{
    string? clientVersion = null;
    string? currentContext = null;

    try
    {
        var versionJson = await RunCommandArgs("kubectl", "version", "--client", "--output", "json");
        using var vDoc = JsonDocument.Parse(versionJson);
        clientVersion = vDoc.RootElement.GetProperty("clientVersion").GetProperty("gitVersion").GetString();
    }
    catch { }

    try { currentContext = (await RunCommand("kubectl", "config current-context")).Trim(); } catch { }

    var contexts = new List<object>();
    try
    {
        var ctxOutput = await RunCommand("kubectl", "config get-contexts --no-headers");
        foreach (var line in ctxOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var isCurrent = line.TrimStart().StartsWith("*");
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var offset = isCurrent ? 1 : 0;  // skip the '*' token
            var name    = parts.Length > offset     ? parts[offset]     : "";
            var cluster = parts.Length > offset + 1 ? parts[offset + 1] : "";
            var ns      = parts.Length > offset + 3 ? parts[offset + 3] : "";
            if (!string.IsNullOrEmpty(name))
                contexts.Add(new { name, cluster, ns, isCurrent });
        }
    }
    catch { }

    var nodes = new List<object>();
    try
    {
        var nodesOutput = await RunCommand("kubectl", "get nodes --no-headers");
        foreach (var line in nodesOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4)
                nodes.Add(new { name = parts[0], status = parts[1], roles = parts[2], age = parts[3], version = parts.Length > 4 ? parts[4] : "" });
        }
    }
    catch { }

    return Results.Ok(new
    {
        clientVersion,
        currentContext,
        kubectlPath = Which("kubectl"),
        contextCount = contexts.Count,
        nodeCount = nodes.Count,
        contexts,
        nodes,
        timestamp = DateTime.UtcNow
    });
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
