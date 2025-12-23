using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO.Compression;
using System.Net.Http;

namespace BuildPortAudio;

internal static class Program {
    private const string PortAudioVersion = "v19.7.0";
    private const string CMakeVersion = "3.28.1";
    private const string GitVersion = "2.43.0";
    
    private static readonly HttpClient HttpClient = new();
    
    private static async Task<int> Main(string[] args) {
        string? architecture = args.Length > 0 ? args[0] : null;
        
        try {
            // Find project root by looking for the solution file
            string currentDir = AppContext.BaseDirectory;
            string projectRoot = FindProjectRoot(currentDir);
            string buildDir = Path.Combine(projectRoot, "build", "portaudio");
            string toolsDir = Path.Combine(projectRoot, "build", "tools");
            string installDir = Path.Combine(projectRoot, "src", "Bufdio.Spice86", "runtimes");
            
            Console.WriteLine($"Building PortAudio {PortAudioVersion} for {GetPlatformName()}...");
            
            // Detect platform and RID
            (string rid, string libName, string cmakeArgs) = DetectPlatformAndGetConfig(architecture);
            
            Console.WriteLine($"Platform: {GetPlatformName()}");
            Console.WriteLine($"RID: {rid}");
            Console.WriteLine($"Architecture: {RuntimeInformation.OSArchitecture}");
            
            // Check if library already exists (cached build result)
            string targetLibPath = Path.Combine(installDir, rid, "native", libName);
            if (File.Exists(targetLibPath)) {
                Console.WriteLine($"✓ PortAudio library already cached at: {targetLibPath}");
                Console.WriteLine("Skipping build (using cached library).");
                return 0;
            }
            
            Console.WriteLine("Library not found in cache. Building from source...");
            
            // Ensure build tools are available (download if needed)
            string cmakePath = await EnsureToolAvailable(toolsDir, "cmake", CMakeVersion);
            string gitPath = await EnsureToolAvailable(toolsDir, "git", GitVersion);
            
            // Clone PortAudio if needed (cache source)
            await ClonePortAudioIfNeeded(buildDir, gitPath);
            
            // Check for platform-specific dependencies
            CheckPlatformDependencies();
            
            // Build PortAudio
            BuildPortAudio(buildDir, cmakeArgs, cmakePath);
            
            // Copy to runtimes directory
            CopyLibraryToRuntimes(buildDir, installDir, rid, libName);
            
            Console.WriteLine();
            Console.WriteLine("✓ PortAudio built successfully and cached!");
            Console.WriteLine($"✓ Library installed to: {Path.Combine(installDir, rid, "native")}");
            Console.WriteLine();
            
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine();
            Console.Error.WriteLine("╔═══════════════════════════════════════════════════╗");
            Console.Error.WriteLine("║  ERROR: Failed to build PortAudio                 ║");
            Console.Error.WriteLine("╚═══════════════════════════════════════════════════╝");
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Error: {ex.Message}");
            if (ex.InnerException != null) {
                Console.Error.WriteLine($"Details: {ex.InnerException.Message}");
            }
            Console.Error.WriteLine();
            Console.Error.WriteLine("This build failure will stop the compilation.");
            Console.Error.WriteLine();
            return 1;
        }
    }
    
    private static string FindProjectRoot(string startPath) {
        string? currentDir = startPath;
        while (currentDir != null) {
            if (File.Exists(Path.Combine(currentDir, "Spice86.sln")) ||
                File.Exists(Path.Combine(currentDir, "src", "Spice86.sln"))) {
                return currentDir;
            }
            currentDir = Directory.GetParent(currentDir)?.FullName;
        }
        throw new InvalidOperationException("Could not find project root (Spice86.sln)");
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
    
    private static async Task<string> EnsureToolAvailable(string toolsDir, string toolName, string version) {
        string toolDir = Path.Combine(toolsDir, toolName);
        string markerFile = Path.Combine(toolDir, $".version-{version}");
        
        // Check if tool is already cached
        if (File.Exists(markerFile)) {
            Console.WriteLine($"✓ {toolName} {version} found in cache");
            return GetToolExecutable(toolDir, toolName);
        }
        
        Console.WriteLine($"Downloading {toolName} {version}...");
        Directory.CreateDirectory(toolDir);
        
        string downloadUrl = GetToolDownloadUrl(toolName, version);
        string downloadPath = Path.Combine(toolsDir, $"{toolName}-{version}.zip");
        
        try {
            // Download tool
            using (HttpResponseMessage response = await HttpClient.GetAsync(downloadUrl)) {
                response.EnsureSuccessStatusCode();
                await using FileStream fs = File.Create(downloadPath);
                await response.Content.CopyToAsync(fs);
            }
            
            // Extract tool
            Console.WriteLine($"Extracting {toolName}...");
            ZipFile.ExtractToDirectory(downloadPath, toolDir, overwriteFiles: true);
            
            // Create version marker
            await File.WriteAllTextAsync(markerFile, version);
            
            // Cleanup download
            File.Delete(downloadPath);
            
            Console.WriteLine($"✓ {toolName} {version} installed and cached");
            return GetToolExecutable(toolDir, toolName);
        } catch (Exception ex) {
            throw new InvalidOperationException($"Failed to download/extract {toolName}: {ex.Message}", ex);
        }
    }
    
    private static string GetToolDownloadUrl(string toolName, string version) {
        string platform = GetPlatformName().ToLower();
        string arch = RuntimeInformation.OSArchitecture.ToString().ToLower();
        
        return toolName switch {
            "cmake" => $"https://github.com/Kitware/CMake/releases/download/v{version}/cmake-{version}-{GetCMakePlatformString()}.zip",
            "git" => $"https://github.com/git-for-windows/git/releases/download/v{version}.windows.1/MinGit-{version}-64-bit.zip",
            _ => throw new NotSupportedException($"Unknown tool: {toolName}")
        };
    }
    
    private static string GetCMakePlatformString() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return RuntimeInformation.OSArchitecture == Architecture.X64 ? "windows-x86_64" : "windows-i386";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            return RuntimeInformation.OSArchitecture == Architecture.X64 ? "linux-x86_64" : "linux-aarch64";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            return "macos-universal";
        }
        throw new PlatformNotSupportedException();
    }
    
    private static string GetToolExecutable(string toolDir, string toolName) {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            string exeName = toolName == "cmake" ? "cmake.exe" : "git.exe";
            string binPath = Path.Combine(toolDir, "bin", exeName);
            if (File.Exists(binPath)) return binPath;
            
            // Try cmake-x.x.x-platform/bin structure
            foreach (string dir in Directory.GetDirectories(toolDir)) {
                binPath = Path.Combine(dir, "bin", exeName);
                if (File.Exists(binPath)) return binPath;
            }
        } else {
            string binPath = Path.Combine(toolDir, "bin", toolName);
            if (File.Exists(binPath)) return binPath;
            
            foreach (string dir in Directory.GetDirectories(toolDir)) {
                binPath = Path.Combine(dir, "bin", toolName);
                if (File.Exists(binPath)) return binPath;
            }
        }
        
        throw new FileNotFoundException($"{toolName} executable not found in {toolDir}");
    }
    
    private static void CheckPlatformDependencies() {
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
    
    private static async Task ClonePortAudioIfNeeded(string buildDir, string gitPath) {
        if (Directory.Exists(buildDir)) {
            Console.WriteLine("✓ PortAudio source already cached.");
            return;
        }
        
        Console.WriteLine("Cloning PortAudio repository...");
        Directory.CreateDirectory(Path.GetDirectoryName(buildDir)!);
        
        if (!RunCommand(gitPath, $"clone --depth 1 --branch {PortAudioVersion} https://github.com/PortAudio/portaudio.git \"{buildDir}\"")) {
            throw new InvalidOperationException("Failed to clone PortAudio repository");
        }
        Console.WriteLine("✓ PortAudio source cached");
    }
    
    private static void BuildPortAudio(string buildDir, string cmakeArgs, string cmakePath) {
        string buildSubDir = Path.Combine(buildDir, "build");
        
        Console.WriteLine("Configuring CMake...");
        if (!RunCommand(cmakePath, $"-B \"{buildSubDir}\" -S \"{buildDir}\" {cmakeArgs} -DCMAKE_INSTALL_PREFIX=install", buildDir)) {
            throw new InvalidOperationException("CMake configuration failed");
        }
        
        Console.WriteLine("Building PortAudio...");
        if (!RunCommand(cmakePath, $"--build \"{buildSubDir}\" --config Release", buildDir)) {
            throw new InvalidOperationException("Build failed");
        }
        
        Console.WriteLine("Installing PortAudio...");
        if (!RunCommand(cmakePath, $"--install \"{buildSubDir}\" --config Release", buildDir)) {
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
