namespace Spice86.Tests.CpuTests.SingleStepTests.TestParsing;

using Spice86.Tests.CpuTests.SingleStepTests.TestParsing.Model;

using System.Text.Json;

using Xunit;

/// <summary>
/// Unit tests for CpuTest JSON deserialization
/// </summary>
public class CpuTestJsonTest {
    [Fact]
    public void TestInitialRegisters_AllFieldsRequired() {
        // Missing a required field (eax)
        string json = """
        {
            "idx": 0,
            "name": "test",
            "bytes": [],
            "initial": {
                "regs": {
                    "ebx": 1, "ecx": 2, "edx": 3,
                    "cs": 0, "ss": 0, "fs": 0, "gs": 0, "ds": 0, "es": 0,
                    "esp": 100, "ebp": 200, "esi": 300, "edi": 400,
                    "eip": 0, "eflags": 0
                },
                "ram": []
            },
            "final": {
                "regs": {},
                "ram": []
            },
            "hash": "test"
        }
        """;

        var ex = Assert.Throws<InvalidTestException>(() => CpuTest.FromJson(json));
        Assert.Contains("Missing required register", ex.Message);
        Assert.Contains("eax", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public void TestInitialRegisters_16BitNames() {
        // Test that 16-bit names (ax, bx, etc.) work
        string json = """
        {
            "idx": 0,
            "name": "test",
            "bytes": [],
            "initial": {
                "regs": {
                    "ax": 1, "bx": 2, "cx": 3, "dx": 4,
                    "cs": 10, "ss": 20, "fs": 30, "gs": 40, "ds": 50, "es": 60,
                    "sp": 100, "bp": 200, "si": 300, "di": 400,
                    "ip": 500, "flags": 514
                },
                "ram": []
            },
            "final": {
                "regs": {},
                "ram": []
            },
            "hash": "test"
        }
        """;

        var test = CpuTest.FromJson(json);

        Assert.NotNull(test);
        Assert.Equal(1u, test.Initial.Registers.EAX);
        Assert.Equal(2u, test.Initial.Registers.EBX);
        Assert.Equal(3u, test.Initial.Registers.ECX);
        Assert.Equal(4u, test.Initial.Registers.EDX);
        Assert.Equal(10, test.Initial.Registers.CS);
        Assert.Equal(20, test.Initial.Registers.SS);
        Assert.Equal(100u, test.Initial.Registers.ESP);
        Assert.Equal(200u, test.Initial.Registers.EBP);
        Assert.Equal(300u, test.Initial.Registers.ESI);
        Assert.Equal(400u, test.Initial.Registers.EDI);
        Assert.Equal(500, test.Initial.Registers.EIP);
        Assert.Equal(514u, test.Initial.Registers.EFlags);
    }

    [Fact]
    public void TestInitialRegisters_32BitNames() {
        // Test that 32-bit names work
        string json = """
        {
            "idx": 0,
            "name": "test",
            "bytes": [],
            "initial": {
                "regs": {
                    "eax": 305419896, "ebx": 2882400000, "ecx": 4294967295, "edx": 0,
                    "cs": 4096, "ss": 8192, "fs": 12288, "gs": 16384, "ds": 20480, "es": 24576,
                    "esp": 4294967280, "ebp": 4660, "esi": 22136, "edi": 39612,
                    "eip": 256, "eflags": 582
                },
                "ram": []
            },
            "final": {
                "regs": {},
                "ram": []
            },
            "hash": "test"
        }
        """;

        var test = CpuTest.FromJson(json);

        Assert.NotNull(test);
        Assert.Equal(305419896u, test.Initial.Registers.EAX);
        Assert.Equal(2882400000u, test.Initial.Registers.EBX);
        Assert.Equal(4294967295u, test.Initial.Registers.ECX);
        Assert.Equal(0u, test.Initial.Registers.EDX);
        Assert.Equal(256, test.Initial.Registers.EIP);
    }

    [Fact]
    public void TestInitialRegisters_UshortValidation() {
        // CS value exceeds ushort.MaxValue
        string json = """
        {
            "idx": 0,
            "name": "test",
            "bytes": [],
            "initial": {
                "regs": {
                    "eax": 0, "ebx": 0, "ecx": 0, "edx": 0,
                    "cs": 70000, "ss": 0, "fs": 0, "gs": 0, "ds": 0, "es": 0,
                    "esp": 0, "ebp": 0, "esi": 0, "edi": 0,
                    "eip": 0, "eflags": 0
                },
                "ram": []
            },
            "final": {
                "regs": {},
                "ram": []
            },
            "hash": "test"
        }
        """;

        var ex = Assert.Throws<InvalidTestException>(() => CpuTest.FromJson(json));
        Assert.Contains("exceeds ushort.MaxValue", ex.Message);
    }

    [Fact]
    public void TestFinalRegisters_PartialSpecification() {
        string json = """
        {
            "idx": 0,
            "name": "test",
            "bytes": [],
            "initial": {
                "regs": {
                    "eax": 100, "ebx": 200, "ecx": 300, "edx": 400,
                    "cs": 0, "ss": 0, "fs": 0, "gs": 0, "ds": 0, "es": 0,
                    "esp": 1000, "ebp": 2000, "esi": 3000, "edi": 4000,
                    "eip": 0, "eflags": 0
                },
                "ram": []
            },
            "final": {
                "regs": {
                    "eax": 999,
                    "cs": 4096,
                    "eip": 256
                },
                "ram": []
            },
            "hash": "test123"
        }
        """;

        var test = CpuTest.FromJson(json);

        Assert.NotNull(test);
        // Check that explicitly set registers have their new values
        Assert.Equal(999u, test.Final.Registers.EAX);
        Assert.Equal(4096, test.Final.Registers.CS);
        Assert.Equal(256, test.Final.Registers.EIP);
        // Check that unset registers were copied from initial
        Assert.Equal(200u, test.Final.Registers.EBX);
        Assert.Equal(300u, test.Final.Registers.ECX);
    }

    [Fact]
    public void TestCaseInsensitiveMatching() {
        // JSON property names are case-sensitive, but our aliases support both 16/32-bit names
        string json = """
        {
            "idx": 0,
            "name": "test",
            "bytes": [],
            "initial": {
                "regs": {
                    "eax": 1, "ebx": 2, "ecx": 3, "edx": 4,
                    "cs": 0, "ss": 0, "fs": 0, "gs": 0, "ds": 0, "es": 0,
                    "esp": 100, "ebp": 200, "esi": 300, "edi": 400,
                    "eip": 0, "eflags": 0
                },
                "ram": []
            },
            "final": {
                "regs": {},
                "ram": []
            },
            "hash": "test"
        }
        """;

        var test = CpuTest.FromJson(json);

        Assert.NotNull(test);
        Assert.Equal(1u, test.Initial.Registers.EAX);
        Assert.Equal(2u, test.Initial.Registers.EBX);
        Assert.Equal(3u, test.Initial.Registers.ECX);
    }

    [Fact]
    public void TestPopulateFinalFromInitial() {
        string json = """
        {
            "idx": 0,
            "name": "test",
            "bytes": [],
            "initial": {
                "regs": {
                    "eax": 100, "ebx": 200, "ecx": 300, "edx": 400,
                    "cs": 4096, "ss": 8192, "fs": 12288, "gs": 16384, "ds": 20480, "es": 24576,
                    "esp": 1000, "ebp": 2000, "esi": 3000, "edi": 4000,
                    "eip": 256, "eflags": 514
                },
                "ram": []
            },
            "final": {
                "regs": {
                    "eax": 999,
                    "eip": 512
                },
                "ram": []
            },
            "hash": "test"
        }
        """;

        var test = CpuTest.FromJson(json);

        // Fields that were set should keep their values
        Assert.Equal(999u, test.Final.Registers.EAX);
        Assert.Equal(512, test.Final.Registers.EIP);

        // Fields that weren't set should be copied from initial
        Assert.Equal(200u, test.Final.Registers.EBX);
        Assert.Equal(300u, test.Final.Registers.ECX);
        Assert.Equal(400u, test.Final.Registers.EDX);
        Assert.Equal(4096, test.Final.Registers.CS);
        Assert.Equal(8192, test.Final.Registers.SS);
        Assert.Equal(1000u, test.Final.Registers.ESP);
        Assert.Equal(2000u, test.Final.Registers.EBP);
        Assert.Equal(3000u, test.Final.Registers.ESI);
        Assert.Equal(4000u, test.Final.Registers.EDI);
        Assert.Equal(514u, test.Final.Registers.EFlags);
    }

    [Fact]
    public void TestCpuTest_IgnoresQueue() {
        // Test that queue property is ignored during deserialization
        string json = """
        {
            "idx": 0,
            "name": "test with queue",
            "bytes": [144],
            "initial": {
                "regs": {
                    "eax": 0, "ebx": 0, "ecx": 0, "edx": 0,
                    "cs": 0, "ss": 0, "fs": 0, "gs": 0, "ds": 0, "es": 0,
                    "esp": 0, "ebp": 0, "esi": 0, "edi": 0,
                    "eip": 0, "eflags": 0
                },
                "ram": [],
                "queue": [1, 2, 3]
            },
            "final": {
                "regs": {
                    "eip": 1
                },
                "ram": [],
                "queue": [4, 5, 6]
            },
            "hash": "abc123"
        }
        """;

        var test = CpuTest.FromJson(json);

        Assert.NotNull(test);
        Assert.Equal("test with queue", test.Name);
        // Queue should be ignored, not cause errors
        Assert.Equal(1, test.Final.Registers.EIP);
    }
}
