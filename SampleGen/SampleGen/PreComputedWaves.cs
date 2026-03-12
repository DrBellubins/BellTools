namespace SampleGen;

public static class PreComputedWaves
{
    // Power-of-two table sizes allow faster wrapping via bitmasking.
    // 4096 is a reasonable default for audio; 16384 gives higher fidelity.
    public const int TableSize = 4096;
    public const int TableMask = TableSize - 1;

    public static readonly float[] Sine = new float[TableSize];
    public static readonly float[] Saw = new float[TableSize];
    public static readonly float[] Square = new float[TableSize];
    public static readonly float[] Triangle = new float[TableSize];

    // Call this once during startup (or in a static constructor if you prefer).
    public static void Initialize()
    {
        FillSine(Sine);
        FillSaw(Saw);
        FillSquare(Square);
        FillTriangle(Triangle);
    }

    public static float ReadNoInterp(float[] table, int phaseIndex)
    {
        return table[phaseIndex & TableMask];
    }

    public static float ReadLinear(float[] table, float phase)
    {
        // phase is expected in [0, TableSize)
        int i0 = (int)phase;
        int i1 = (i0 + 1) & TableMask;

        float frac = phase - i0;
        float a = table[i0 & TableMask];
        float b = table[i1];

        return a + ((b - a) * frac);
    }

    private static void FillSine(float[] dst)
    {
        for (int i = 0; i < dst.Length; i++)
        {
            double t = (double)i / dst.Length; // [0, 1)
            dst[i] = (float)Math.Sin(t * 2.0 * Math.PI);
        }
    }

    private static void FillSaw(float[] dst)
    {
        // Naive saw: ramps -1 to +1 over one cycle.
        // For band-limited synthesis you would later want a minBLEP/polyBLEP or multi-table approach.
        int n = dst.Length;

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;         // [0,1)
            dst[i] = (2.0f * t) - 1.0f;     // [-1,1)
        }
    }

    private static void FillSquare(float[] dst)
    {
        int n = dst.Length;
        int half = n / 2;

        for (int i = 0; i < n; i++)
            dst[i] = (i < half) ? 1.0f : -1.0f;
    }

    private static void FillTriangle(float[] dst)
    {
        // Triangle with amplitude in [-1, 1].
        // Piecewise linear: rises then falls symmetrically.
        int n = dst.Length;

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n; // [0,1)

            // 0..1 mapped to triangle:
            // 0 -> -1, 0.25 -> 0, 0.5 -> +1, 0.75 -> 0, 1 -> -1
            float tri = 0.0f;

            if (t < 0.25f)
                tri = -1.0f + (t / 0.25f) * 1.0f;
            else if (t < 0.75f)
                tri = 0.0f + ((0.5f - MathF.Abs(t - 0.5f)) / 0.25f) * 1.0f;
            else
                tri = -1.0f + ((1.0f - t) / 0.25f) * 1.0f;

            dst[i] = tri;
        }
    }
}