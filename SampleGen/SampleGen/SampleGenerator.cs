namespace SampleGen;

public class SampleGenerator
{
    public static string SamplePath = Path.Combine(AppContext.BaseDirectory, "Samples");
    
    public static void Initialize(string[] args)
    {
        if (string.IsNullOrEmpty(args[0]))
        {
            Debug.Error("No arguments given!");
            return;
        }

        if (!Directory.Exists(SamplePath))
            Directory.CreateDirectory(SamplePath);
        
        PreComputedWaves.Initialize();
    }

    // Returns left and right channel sample
    public static (List<float>, List<float>) GenerateSample(SampleType type, float frequency,
        int sampleRate, int amount, float lengthSeconds, float detune, bool randomDetuneDistro = false)
    {
        if (sampleRate <= 0)
            Debug.Error("Sample rate must be positive.");

        if (lengthSeconds <= 0.0f)
            Debug.Error("Length in seconds must be positive.");

        if (amount <= 0)
            Debug.Error("Amount must be positive.");

        int sampleCount = (int)MathF.Round(sampleRate * lengthSeconds);
        
        if (sampleCount <= 0)
            return (new List<float>(), new List<float>());

        float[] wavetable = type switch
        {
            SampleType.Sine => PreComputedWaves.Sine,
            SampleType.Saw => PreComputedWaves.Saw,
            SampleType.Square => PreComputedWaves.Square,
            SampleType.Triangle => PreComputedWaves.Triangle,
            _ => PreComputedWaves.Sine
        };

        // Each oscillator writes to a private buffer to avoid synchronization.
        float[][] partials = new float[amount][];

        Parallel.For(
            fromInclusive: 0,
            toExclusive: amount,
            body: i =>
            {
                float[] local = new float[sampleCount];

                // Detune factor in [-detune, +detune].
                // Interpreting "detune" as a fractional frequency offset (e.g., 0.01 => +/- 1%).
                float detuneFactor = 0.0f;
                if (amount > 1)
                {
                    float t = (float)i / (amount - 1); // [0, 1]
                    detuneFactor = ((t * 2.0f) - 1.0f) * detune; // [-detune, +detune]
                }

                float oscFreq = frequency * (1.0f + detuneFactor);

                // Phase increment in table-space per sample.
                // phase is in [0, TableSize) but will wrap naturally via masking in ReadLinear.
                float phase = 0.0f;
                float phaseInc = (oscFreq * PreComputedWaves.TableSize) / sampleRate;

                for (int s = 0; s < sampleCount; s++)
                {
                    local[s] = PreComputedWaves.ReadLinear(wavetable, phase);

                    phase += phaseInc;

                    // Keep phase bounded to avoid float growth over long renders.
                    // This is cheaper than a modulo and sufficient for correctness.
                    if (phase >= PreComputedWaves.TableSize)
                    {
                        phase -= PreComputedWaves.TableSize;
                        if (phase >= PreComputedWaves.TableSize)
                        {
                            phase = phase % PreComputedWaves.TableSize;
                        }
                    }
                    else if (phase < 0.0f)
                    {
                        phase += PreComputedWaves.TableSize;
                    }
                }

                partials[i] = local;
            });

        // Sum partial buffers into final output.
        float[] mixed = new float[sampleCount];
        for (int i = 0; i < amount; i++)
        {
            float[] local = partials[i];
            for (int s = 0; s < sampleCount; s++)
            {
                mixed[s] += local[s];
            }
        }

        // Normalize by number of oscillators to keep amplitude roughly consistent.
        float inv = 1.0f / amount;
        for (int s = 0; s < sampleCount; s++)
        {
            mixed[s] *= inv;
        }

        return new List<float>(mixed);
    }

    public static void SaveSample(List<float> samples, string path)
    {
        // samples.Count is the sample rate!
    }
}

public enum SampleType
{
    Sine,
    Saw,
    Square,
    Triangle
}