namespace Spice86.Tests;

using FluentAssertions;

using System.Reflection;
using System.Runtime.Loader;

using Xunit;

public class GeneratedCodeProbesTest {
    private const string Source = """
        namespace Spice86.Generated;

        public static class Sample {
            public static int Run(int value) {
                int result = 0;
                if (value == 1) {
                    result = 1;
                } else {
                    result = 2;
                }
                switch (value) {
                    case 1:
                        result += 10;
                        break;
                    default:
                        throw FailAsUntested("never");
                }
                while (true) {
                    break;
                }
                goto done;
            done:
                return result;
            }

            private static System.Exception FailAsUntested(string message) => new System.InvalidOperationException(message);
        }
        """;

    [Fact]
    public void EveryStatementOfBlocksAndSectionsGetsASiteInPreOrder() {
        // Act
        InstrumentedGeneratedSource instrumented = GeneratedCodeProbes.Instrument(Source);

        // Assert
        instrumented.Sites.Select(site => site.Context).Should().Equal(
            "method", "method", "then", "else", "method", "case", "case", "default", "method", "whileTrue", "method", "method");
        instrumented.Sites.Select(site => site.Kind).Should().Equal(
            "LocalDeclarationStatement", "IfStatement", "ExpressionStatement", "ExpressionStatement", "SwitchStatement",
            "ExpressionStatement", "BreakStatement", "failAsUntested", "WhileStatement", "BreakStatement", "GotoStatement",
            "ReturnStatement");
        instrumented.Sites.Should().OnlyContain(site => site.Method == "Run");
        instrumented.Sites[0].Line.Should().Be(5);
        instrumented.Sites[11].Text.Should().Be("return result;");
    }

    [Fact]
    public void InstrumentedSourceCompilesAndRecordsTheStatementsThatRan() {
        // Arrange
        InstrumentedGeneratedSource instrumented = GeneratedCodeProbes.Instrument(Source);
        GeneratedCompilation compilation = new GeneratedOverrideCompiler().Compile(instrumented.Source);
        compilation.EmitResult.Success.Should().BeTrue(string.Join(Environment.NewLine, compilation.EmitResult.Diagnostics));
        compilation.PeStream.Position = 0;
        AssemblyLoadContext context = new("probes-test", isCollectible: true);
        Assembly assembly = context.LoadFromStream(compilation.PeStream);
        MethodInfo run = assembly.GetTypes().Single(type => type.Name == "Sample").GetMethod("Run")
            ?? throw new InvalidOperationException("Sample.Run is missing.");

        // Act
        object? result = run.Invoke(null, [1]);
        IReadOnlyList<bool> hits = GeneratedCodeProbes.ReadHits(assembly);

        // Assert
        result.Should().Be(11);
        hits.Should().Equal(true, true, true, false, true, true, true, false, true, true, true, true);
        context.Unload();
    }
}
