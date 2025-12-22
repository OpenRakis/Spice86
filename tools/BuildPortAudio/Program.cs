using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BuildPortAudio;

internal static class Program {
    private const string PortAudioVersion = "v19.7.0";
    
    private static int Main(string[] args) {
        string? architecture = args.Length > 0 ? args[0] : null;
        
        try {
            string scriptDir = AppContext.BaseDirectory;
            string projectRoot = Path.GetFullPath(Path.Combine(scriptDir, "..", "..", ".."));
            string buildDir = Path.Combine(projectRoot, "build", "portaudio");
            string installDir = Path.Combine(projectRoot, "src", "Bufdio.Spice86", "runtimes");
            
            Console.WriteLine($"Building PortAudio {PortAudioVersion} for local development...");
            
            // Detect platform and RID
            (string rid, string libName, string cmakeArgs) = DetectPlatformAndGetConfig(architecture);
            
            Console.WriteLine($"Platform: {GetPlatformName()}");
            Console.WriteLine($"RID: {rid}");
            Console.WriteLine($"Library: {libName}");
            
            // Check dependencies
            CheckDependencies();
            
            // Clone PortAudio if needed
            ClonePortAudioIfNeeded(buildDir);
            
            // Build PortAudio
            BuildPortAudio(buildDir, cmakeArgs);
            
            // Copy to runtimes directory
            CopyLibraryToRuntimes(buildDir, installDir, rid, libName);
            
            Console.WriteLine();
            Console.WriteLine("✓ PortAudio built successfully!");
            Console.WriteLine($"✓ Library installed to: {Path.Combine(installDir, rid, "native")}");
            Console.WriteLine();
            Console.WriteLine("You can now build and debug Spice86 with audio support.");
            
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
    
    private static string GetPlatformName() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return "Windows";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            return "Linux";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            return "macOS";
        }
        throw new PlatformNotSupportedException("Unsupported platform");
    }
    
    private static (string rid, string libName, string cmakeArgs) DetectPlatformAndGetConfig(string? architecture) {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return GetWindowsConfig(architecture);
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            return GetLinuxConfig();
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            return GetMacOSConfig();
        }
        throw new PlatformNotSupportedException("Unsupported platform");
    }
    
    private static (string rid, string libName, string cmakeArgs) GetWindowsConfig(string? architecture) {
        string arch = architecture?.ToLower() ?? "x64";
        string cmakeArch = arch switch {
            "x64" => "x64",
            "x86" => "Win32",
            "arm64" => "ARM64",
            _ => throw new ArgumentException($"Unsupported architecture: {architecture}")
        };
        
        string rid = $"win-{arch}";
        string cmakeArgs = $"-A {cmakeArch} -DCMAKE_BUILD_TYPE=Release -DPA_BUILD_SHARED=ON -DPA_BUILD_STATIC=OFF " +
                          "-DPA_USE_ASIO=OFF -DPA_USE_DS=OFF -DPA_USE_WMME=OFF -DPA_USE_WASAPI=ON -DPA_USE_WDMKS=OFF";
        
        return (rid, "libportaudio.dll", cmakeArgs);
    }
    
    private static (string rid, string libName, string cmakeArgs) GetLinuxConfig() {
        string arch = RuntimeInformation.OSArchitecture.ToString().ToLower();
        string rid = arch switch {
            "x64" => "linux-x64",
            "arm64" => "linux-arm64",
            _ => throw new PlatformNotSupportedException($"Unsupported Linux architecture: {arch}")
        };
        
        string cmakeArgs = "-DCMAKE_BUILD_TYPE=Release -DPA_BUILD_SHARED=ON -DPA_BUILD_STATIC=OFF " +
                          "-DPA_USE_ALSA=ON -DPA_USE_JACK=OFF -DPA_USE_OSS=OFF";
        
        return (rid, "libportaudio.so.2", cmakeArgs);
    }
    
    private static (string rid, string libName, string cmakeArgs) GetMacOSConfig() {
        string arch = RuntimeInformation.OSArchitecture.ToString().ToLower();
        string rid = arch switch {
            "x64" => "osx-x64",
            "arm64" => "osx-arm64",
            _ => throw new PlatformNotSupportedException($"Unsupported macOS architecture: {arch}")
        };
        
        string cmakeArchArg = arch == "x64" ? "x86_64" : "arm64";
        string cmakeArgs = $"-DCMAKE_BUILD_TYPE=Release -DPA_BUILD_SHARED=ON -DPA_BUILD_STATIC=OFF " +
                          $"-DCMAKE_OSX_ARCHITECTURES={cmakeArchArg}";
        
        return (rid, "libportaudio.2.dylib", cmakeArgs);
    }
    
    private static void CheckDependencies() {
        if (!IsCommandAvailable("cmake")) {
            throw new InvalidOperationException("CMake is not installed. Please install CMake first.");
        }
        
        if (!IsCommandAvailable("git")) {
            throw new InvalidOperationException("Git is not installed. Please install Git first.");
        }
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            Console.WriteLine("Checking for ALSA development libraries...");
            if (!IsCommandAvailable("pkg-config") || !RunCommand("pkg-config", "--exists alsa", quiet: true)) {
                throw new InvalidOperationException(
                    "ALSA development libraries not found. Please install them with: sudo apt-get install libasound2-dev");
            }
        }
    }
    
    private static bool IsCommandAvailable(string command) {
        try {
            ProcessStartInfo psi = new ProcessStartInfo {
                FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using Process? process = Process.Start(psi);
            process?.WaitForExit();
            return process?.ExitCode == 0;
        } catch {
            return false;
        }
    }
    
    private static void ClonePortAudioIfNeeded(string buildDir) {
        if (Directory.Exists(buildDir)) {
            Console.WriteLine("PortAudio repository already exists.");
            return;
        }
        
        Console.WriteLine("Cloning PortAudio repository...");
        Directory.CreateDirectory(Path.GetDirectoryName(buildDir)!);
        
        if (!RunCommand("git", $"clone --depth 1 --branch {PortAudioVersion} https://github.com/PortAudio/portaudio.git \"{buildDir}\"")) {
            throw new InvalidOperationException("Failed to clone PortAudio repository");
        }
    }
    
    private static void BuildPortAudio(string buildDir, string cmakeArgs) {
        string buildSubDir = Path.Combine(buildDir, "build");
        
        Console.WriteLine("Configuring CMake...");
        if (!RunCommand("cmake", $"-B \"{buildSubDir}\" -S \"{buildDir}\" {cmakeArgs} -DCMAKE_INSTALL_PREFIX=install", buildDir)) {
            throw new InvalidOperationException("CMake configuration failed");
        }
        
        Console.WriteLine("Building...");
        if (!RunCommand("cmake", $"--build \"{buildSubDir}\" --config Release", buildDir)) {
            throw new InvalidOperationException("Build failed");
        }
        
        Console.WriteLine("Installing...");
        if (!RunCommand("cmake", $"--install \"{buildSubDir}\" --config Release", buildDir)) {
            throw new InvalidOperationException("Installation failed");
        }
    }
    
    private static void CopyLibraryToRuntimes(string buildDir, string installDir, string rid, string libName) {
        string outputDir = Path.Combine(installDir, rid, "native");
        Directory.CreateDirectory(outputDir);
        
        string sourceDir;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            sourceDir = Path.Combine(buildDir, "install", "bin");
        } else {
            sourceDir = Path.Combine(buildDir, "install", "lib");
        }
        
        // Copy library files
        foreach (string sourceFile in Directory.GetFiles(sourceDir, $"{Path.GetFileNameWithoutExtension(libName)}*")) {
            string destFile = Path.Combine(outputDir, Path.GetFileName(sourceFile));
            File.Copy(sourceFile, destFile, true);
            Console.WriteLine($"Copied: {Path.GetFileName(sourceFile)}");
        }
        
        // Create symlink for Linux if needed
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            string targetLib = Path.Combine(outputDir, libName);
            if (!File.Exists(targetLib)) {
                string[] versionedLibs = Directory.GetFiles(outputDir, $"{libName}.*");
                if (versionedLibs.Length > 0) {
                    string versionedLib = versionedLibs[0];
                    string linkName = Path.GetFileName(targetLib);
                    string linkTarget = Path.GetFileName(versionedLib);
                    
                    // Create symlink using ln command
                    RunCommand("ln", $"-sf \"{linkTarget}\" \"{linkName}\"", outputDir);
                }
            }
        }
    }
    
    private static bool RunCommand(string command, string arguments, string? workingDirectory = null, bool quiet = false) {
        try {
            ProcessStartInfo psi = new ProcessStartInfo {
                FileName = command,
                Arguments = arguments,
                WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
                RedirectStandardOutput = quiet,
                RedirectStandardError = quiet,
                UseShellExecute = false,
                CreateNoWindow = quiet
            };
            
            using Process? process = Process.Start(psi);
            if (process == null) {
                return false;
            }
            
            process.WaitForExit();
            return process.ExitCode == 0;
        } catch (Exception ex) {
            if (!quiet) {
                Console.Error.WriteLine($"Failed to run command: {command} {arguments}");
                Console.Error.WriteLine($"Error: {ex.Message}");
            }
            return false;
        }
    }
}
