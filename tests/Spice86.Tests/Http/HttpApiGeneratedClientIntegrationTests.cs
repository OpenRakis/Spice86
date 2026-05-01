namespace Spice86.Tests.Http;

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

using FluentAssertions;

using NSubstitute;

using Xunit;

[Collection(HttpApiServerCollection.Name)]
public sealed partial class HttpApiGeneratedClientIntegrationTests {
    private const string KiotaToolVersion = "1.30.0";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(3);
    private readonly HttpApiServerFixture _fixture;

    public HttpApiGeneratedClientIntegrationTests(HttpApiServerFixture fixture) {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("json", "/openapi/v1.json")]
    [InlineData("yaml", "/openapi/v1.yaml")]
    public async Task KiotaGeneratedDotNetClient_CanBeGeneratedBuiltAndExecuted(string extension, string openApiPath) {
        // Arrange
        _fixture.PauseHandler.IsPaused.Returns(false);
        _fixture.PauseHandler.ClearReceivedCalls();
        TestWorkspace workspace = CreateTestWorkspace(extension);

        try {
            await DownloadOpenApiDocumentAsync(openApiPath, workspace.SpecFilePath);
            string kiotaExecutable = await EnsureKiotaInstalledAsync(workspace.ToolPath);
            string bundleVersion = await GetKiotaBundleVersionAsync(kiotaExecutable, workspace.SpecFilePath);
            CreateConsumerProject(workspace.RootPath, bundleVersion);

            // Act
            ProcessResult generationResult = await GenerateClientAsync(kiotaExecutable, workspace);
            ProcessResult executionResult = await ExecuteConsumerAsync(workspace.ProjectPath);

            // Assert
            generationResult.ExitCode.Should().Be(0, generationResult.CombinedOutput);
            Directory.Exists(workspace.GeneratedClientDirectory).Should().BeTrue();
            executionResult.ExitCode.Should().Be(0, executionResult.CombinedOutput);
            executionResult.StdOut.Should().Contain("status:1234:5678:128:False");
            executionResult.StdOut.Should().Contain("byte:12");
        } finally {
            DeleteDirectoryIfExists(workspace.RootPath);
        }
    }

    private static TestWorkspace CreateTestWorkspace(string extension) {
        string baseTempPath = Path.GetTempPath();
        string rootPath = Path.Join(baseTempPath, "Spice86.Tests", "HttpApiGeneratedClient", Guid.NewGuid().ToString("N"));
        string toolPath = Path.Join(baseTempPath, "Spice86.Tests", "Tools", "Kiota");
        Directory.CreateDirectory(rootPath);
        Directory.CreateDirectory(toolPath);
        return new TestWorkspace(
            rootPath,
            toolPath,
            Path.Join(rootPath, "GeneratedClientConsumer.csproj"),
            Path.Join(rootPath, "Program.cs"),
            Path.Join(rootPath, "Client"),
            Path.Join(rootPath, $"openapi.{extension}"));
    }

    private async Task DownloadOpenApiDocumentAsync(string relativePath, string destinationPath) {
        string document = await _fixture.HttpClient.GetStringAsync(relativePath);
        await File.WriteAllTextAsync(destinationPath, document, Encoding.UTF8);
    }

    private static async Task<string> EnsureKiotaInstalledAsync(string toolPath) {
        string executableName = OperatingSystem.IsWindows() ? "kiota.exe" : "kiota";
        string kiotaExecutable = Path.Join(toolPath, executableName);
        if (File.Exists(kiotaExecutable)) {
            return kiotaExecutable;
        }

        ProcessResult installResult = await RunProcessAsync(
            "dotnet",
            ["tool", "install", "Microsoft.OpenApi.Kiota", "--tool-path", toolPath, "--version", KiotaToolVersion],
            toolPath,
            CommandTimeout);

        installResult.ExitCode.Should().Be(0, installResult.CombinedOutput);
        File.Exists(kiotaExecutable).Should().BeTrue();
        return kiotaExecutable;
    }

    private static async Task<string> GetKiotaBundleVersionAsync(string kiotaExecutable, string specFilePath) {
        ProcessResult infoResult = await RunProcessAsync(
            kiotaExecutable,
            ["info", "-d", specFilePath, "-l", "CSharp"],
            Path.GetDirectoryName(specFilePath)!,
            CommandTimeout);

        infoResult.ExitCode.Should().Be(0, infoResult.CombinedOutput);

        Match match = KiotaBundleVersionRegex().Match(infoResult.StdOut);
        match.Success.Should().BeTrue(infoResult.StdOut);
        return match.Groups[1].Value;
    }

    private static void CreateConsumerProject(string rootPath, string bundleVersion) {
        File.WriteAllText(Path.Join(rootPath, "GeneratedClientConsumer.csproj"), BuildProjectFile(bundleVersion));
        File.WriteAllText(Path.Join(rootPath, "Program.cs"), BuildProgramFile());
    }

    private static string BuildProjectFile(string bundleVersion) => $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Kiota.Bundle" Version="{{bundleVersion}}" />
  </ItemGroup>
</Project>
""";

    private static string BuildProgramFile() => """
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Spice86.Generated.HttpApi;

if (args.Length != 1) {
    throw new InvalidOperationException("Expected a single base URL argument.");
}

var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider()) {
    BaseUrl = args[0]
};
var client = new Spice86HttpApiClient(adapter);

var status = await client.Api.Status.GetAsync();
Console.WriteLine(status is null
    ? "status:null"
    : $"status:{AsInt(status.Cs):X4}:{AsInt(status.Ip):X4}:{AsInt(status.Cycles)}:{status.IsPaused}");

var memoryByte = await client.Api.Memory[64].Byte.GetAsync();
Console.WriteLine(memoryByte is null
    ? "byte:null"
    : $"byte:{AsInt(memoryByte.Value):X2}");

static int AsInt(UntypedNode? value) => value switch {
    UntypedInteger integerValue => integerValue.GetValue(),
    null => throw new InvalidOperationException("Expected an integer value but got null."),
    _ => throw new InvalidOperationException($"Expected an integer value but got {value.GetType().Name}.")
};
""";

    private static async Task<ProcessResult> GenerateClientAsync(string kiotaExecutable, TestWorkspace workspace) {
        return await RunProcessAsync(
            kiotaExecutable,
            [
                "generate",
                "-l", "CSharp",
                "-d", workspace.SpecFilePath,
                "-c", "Spice86HttpApiClient",
                "-n", "Spice86.Generated.HttpApi",
                "-o", workspace.GeneratedClientDirectory,
                "--clean-output"
            ],
            workspace.RootPath,
            CommandTimeout);
    }

    private async Task<ProcessResult> ExecuteConsumerAsync(string projectPath) {
        string baseUrl = _fixture.HttpClient.BaseAddress!.GetLeftPart(UriPartial.Authority);
        return await RunProcessAsync(
            "dotnet",
            ["run", "--project", projectPath, "--", baseUrl],
            Path.GetDirectoryName(projectPath)!,
            CommandTimeout);
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments,
        string workingDirectory, TimeSpan timeout) {
        ProcessStartInfo startInfo = new() {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string argument in arguments) {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        process.Start();

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource cancellationTokenSource = new(timeout);

        try {
            await process.WaitForExitAsync(cancellationTokenSource.Token);
        } catch (OperationCanceledException) {
            if (!process.HasExited) {
                process.Kill(entireProcessTree: true);
            }
            throw new TimeoutException($"Command timed out: {fileName} {string.Join(' ', arguments)}");
        }

        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static void DeleteDirectoryIfExists(string path) {
        if (Directory.Exists(path)) {
            Directory.Delete(path, recursive: true);
        }
    }

    [GeneratedRegex(@"Microsoft\.Kiota\.Bundle\s+(\d+\.\d+\.\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex KiotaBundleVersionRegex();

    private sealed record TestWorkspace(
        string RootPath,
        string ToolPath,
        string ProjectPath,
        string ProgramPath,
        string GeneratedClientDirectory,
        string SpecFilePath);

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr) {
        public string CombinedOutput => $"STDOUT:{Environment.NewLine}{StdOut}{Environment.NewLine}STDERR:{Environment.NewLine}{StdErr}";
    }
}
