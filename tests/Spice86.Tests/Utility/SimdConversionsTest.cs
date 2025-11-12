using static Spice86.Shared.Utils.SimdConversions;

namespace Spice86.Tests.Utility;

using JetBrains.Annotations;

using Spice86.Shared.Utils;

using Xunit;

[TestSubject(typeof(SimdConversions))]
public class SimdConversionsTest {
    private const float DefaultScale = 1.0f / 32768f;

    public static TheoryData<int> SampleLengths() {
        var testCases = new TheoryData<int>();
        testCases.AddRange(0, 1, 7, 8, 15, 16, 31, 32, 33, 64, 4096);
        return testCases;
    }

    public static TheoryData<int, float> ScaleTestCases() {
        var testCases = new TheoryData<int, float>();
        int[] lengths = [0, 1, 4, 7, 8, 15, 16, 31, 32, 64, 4096];
        float[] scales = [DefaultScale, 0.5f, -0.75f, 0f, 1f];

        foreach (int length in lengths) {
            foreach (float scale in scales) {
                testCases.Add(length, scale);
            }
        }

        return testCases;
    }

    [Theory]
    [MemberData(nameof(SampleLengths))]
    public void ConvertInt16ToScaledFloat_AllBackendsAgree(int length) {
        short[] source = CreateSequentialSamples(length);

        float[] scalar = new float[length];
        ConvertInt16ToScaledFloatForTesting(source, scalar, DefaultScale, IntrinsicBackend.Scalar);

        float[] vector = new float[length];
        ConvertInt16ToScaledFloatForTesting(source, vector, DefaultScale, IntrinsicBackend.Vector);
        Assert.Equal(scalar, vector);
    }

    [Theory]
    [MemberData(nameof(ScaleTestCases))]
    public void ScaleInPlace_AllBackendsAgree(int length, float scale) {
        float[] baseline = CreateRandomFloats(length);

        float[] scalar = baseline.ToArray();
        ScaleInPlaceForTesting(scalar, scale, IntrinsicBackend.Scalar);

        float[] vector = baseline.ToArray();
        ScaleInPlaceForTesting(vector, scale, IntrinsicBackend.Vector);
        Assert.Equal(scalar, vector);
    }

    [Theory]
    [MemberData(nameof(ScaleTestCases))]
    public void ConvertUInt8ToScaledFloat_AllBackendsAgree(int length, float scale) {
        byte[] source = CreateSequentialByteSamples(length);

        float[] scalar = new float[length];
        ConvertUInt8ToScaledFloatForTesting(source, scalar, scale, IntrinsicBackend.Scalar);

        float[] vector = new float[length];
        ConvertUInt8ToScaledFloatForTesting(source, vector, scale, IntrinsicBackend.Vector);
        Assert.Equal(scalar, vector);
    }

    private static short[] CreateSequentialSamples(int length) {
        short[] data = new short[length];
        for (int i = 0; i < length; i++) {
            int value = ((i * 97) + 31) & 0xFFFF;
            data[i] = (short)(value - 0x8000);
        }

        return data;
    }

    private static byte[] CreateSequentialByteSamples(int length) {
        byte[] data = new byte[length];
        for (int i = 0; i < length; i++) {
            data[i] = (byte)(((i * 67) + 19) & 0xFF);
        }

        return data;
    }

    private static float[] CreateRandomFloats(int length) {
        var rng = new Random(0xC0FFEE + length);
        float[] data = new float[length];
        for (int i = 0; i < length; i++) {
            data[i] = (float)((rng.NextDouble() * 2.0) - 1.0);
        }

        return data;
    }
}