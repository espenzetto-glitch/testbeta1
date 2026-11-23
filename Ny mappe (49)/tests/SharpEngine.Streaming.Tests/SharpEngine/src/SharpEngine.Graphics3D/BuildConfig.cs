using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpEngine.Graphics3D;

public enum TargetPlatform { Windows, MacOS, Linux }

public enum CpuArchitecture { X86, X64, Arm64 }

public enum CompressionMethod { Default, Zip }

/// <summary>What kind of player to build.</summary>
public enum BuildPlatform { Standalone, WebGL }

public sealed class BuildScene
{
    /// <summary>Scene name relative to the Scenes folder, e.g. "Scenes/MainScene".</summary>
    public string Path { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

/// <summary>Everything the Build Settings window edits. Saved as BuildSettings.json next to the game project.</summary>
public sealed class BuildConfig
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public List<BuildScene> Scenes { get; set; } = new();

    // world streaming: neighbouring chunks (scenes) are loaded around the player at their World Map positions
    public bool StreamWorld { get; set; } = true;

    /// <summary>How many cells around the player stay loaded (1 = 3x3 chunks, 2 = 5x5).</summary>
    public int StreamRadius { get; set; } = 1;

    // player settings
    public string ProductName { get; set; } = "MyGame";
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 720;
    public bool StartFullscreen { get; set; }
    public bool SelfContained { get; set; }

    // build settings
    public BuildPlatform Platform { get; set; } = BuildPlatform.Standalone;
    public TargetPlatform Target { get; set; } = HostPlatform();
    public CpuArchitecture Architecture { get; set; } = HostArchitecture();
    public bool DevelopmentBuild { get; set; }
    public bool CopyPdbFiles { get; set; }
    public bool CreateVisualStudioSolution { get; set; }
    public CompressionMethod Compression { get; set; } = CompressionMethod.Default;

    /// <summary>Empty = Builds/&lt;product&gt;_&lt;platform&gt;_&lt;arch&gt; next to the solution.</summary>
    public string OutputFolder { get; set; } = "";

    public static TargetPlatform HostPlatform() =>
        OperatingSystem.IsWindows() ? TargetPlatform.Windows :
        OperatingSystem.IsMacOS() ? TargetPlatform.MacOS : TargetPlatform.Linux;

    public static CpuArchitecture HostArchitecture() => RuntimeInformation.OSArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X86 => CpuArchitecture.X86,
        System.Runtime.InteropServices.Architecture.Arm64 => CpuArchitecture.Arm64,
        _ => CpuArchitecture.X64
    };

    /// <summary>CPU architectures for which Raylib ships a native library.</summary>
    public static CpuArchitecture[] SupportedArchitectures(TargetPlatform target) => target switch
    {
        TargetPlatform.Windows => new[] { CpuArchitecture.X86, CpuArchitecture.X64 },
        TargetPlatform.MacOS => new[] { CpuArchitecture.X64, CpuArchitecture.Arm64 },
        _ => new[] { CpuArchitecture.X64 }
    };

    /// <summary>.NET runtime identifier such as win-x64, or null for an unsupported combination.</summary>
    public static string? RuntimeIdentifier(TargetPlatform target, CpuArchitecture arch)
    {
        if (!SupportedArchitectures(target).Contains(arch)) return null;
        string os = target switch { TargetPlatform.Windows => "win", TargetPlatform.MacOS => "osx", _ => "linux" };
        string cpu = arch switch { CpuArchitecture.X86 => "x86", CpuArchitecture.Arm64 => "arm64", _ => "x64" };
        return $"{os}-{cpu}";
    }

    /// <summary>Keeps the architecture valid after the target platform changes.</summary>
    public void FixArchitecture()
    {
        var ok = SupportedArchitectures(Target);
        if (!ok.Contains(Architecture)) Architecture = ok.Contains(CpuArchitecture.X64) ? CpuArchitecture.X64 : ok[0];
    }

    public IEnumerable<BuildScene> EnabledScenes => Scenes.Where(s => s.Enabled);

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static BuildConfig FromJson(string json)
    {
        var cfg = JsonSerializer.Deserialize<BuildConfig>(json, Options) ?? new BuildConfig();
        cfg.FixArchitecture();
        return cfg;
    }
}
