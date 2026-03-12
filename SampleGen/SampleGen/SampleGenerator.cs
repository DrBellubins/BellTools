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

    // Returns left and right channel samples
    public static (List<float> left, List<float> right) GenerateSample(
        SampleType type,
        float frequency,
        int sampleRate,
        int amount,
        float lengthSeconds,
        float detune,
        bool randomDetuneDistro = false)
    {
        if (sampleRate <= 0)
        {
            Debug.Error("Sample rate must be positive.");
            return (new List<float>(), new List<float>());
        }

        if (lengthSeconds <= 0.0f)
        {
            Debug.Error("Length in seconds must be positive.");
            return (new List<float>(), new List<float>());
        }

        if (amount <= 0)
        {
            Debug.Error("Amount must be positive.");
            return (new List<float>(), new List<float>());
        }

        int sampleCount = (int)MathF.Round(sampleRate * lengthSeconds);
        if (sampleCount <= 0)
        {
            return (new List<float>(), new List<float>());
        }

        float[] wavetable = type switch
        {
            SampleType.Sine => PreComputedWaves.Sine,
            SampleType.Saw => PreComputedWaves.Saw,
            SampleType.Square => PreComputedWaves.Square,
            SampleType.Triangle => PreComputedWaves.Triangle,
            _ => PreComputedWaves.Sine
        };

        // Per-oscillator partial buffers to avoid locking in the hot loop.
        float[][] partialLeft = new float[amount][];
        float[][] partialRight = new float[amount][];

        Parallel.For(
            fromInclusive: 0,
            toExclusive: amount,
            body: i =>
            {
                float[] localL = new float[sampleCount];
                float[] localR = new float[sampleCount];

                // Thread-safe randomness: create a per-oscillator Random instance.
                // Seed is deterministic across runs for a given i (useful for debugging).
                Random rng = new Random(unchecked(0x2C9277B5 ^ (i * 0x1B873593)));

                // 1) Detune factor (fractional): oscFreq = frequency * (1 + detuneFactor)
                // detuneFactor in [-detune, +detune]
                float detuneFactor;
                if (amount <= 1 || detune == 0.0f)
                {
                    detuneFactor = 0.0f;
                }
                else if (randomDetuneDistro)
                {
                    float u = (float)rng.NextDouble();          // [0, 1)
                    detuneFactor = ((u * 2.0f) - 1.0f) * detune; // [-detune, +detune]
                }
                else
                {
                    float t = (float)i / (amount - 1);           // [0, 1]
                    detuneFactor = ((t * 2.0f) - 1.0f) * detune; // [-detune, +detune]
                }

                float oscFreq = frequency * (1.0f + detuneFactor);

                // 2) Stereo placement: spread oscillators across the field.
                // Pan in [-1, +1]; -1 = left, +1 = right
                float pan;
                if (amount <= 1)
                {
                    pan = 0.0f;
                }
                else
                {
                    float tPan = (float)i / (amount - 1); // [0, 1]
                    pan = (tPan * 2.0f) - 1.0f;           // [-1, 1]
                }

                // Constant-power panning (preserves perceived loudness across pan positions)
                float angle = (pan + 1.0f) * 0.25f * MathF.PI; // map [-1,1] -> [0, pi/2]
                float gainL = MathF.Cos(angle);
                float gainR = MathF.Sin(angle);

                // 3) Random start phase per oscillator (requested).
                float phase;
                if (randomDetuneDistro)
                {
                    // When randomDetuneDistro is enabled, random phase is expected.
                    phase = (float)rng.NextDouble() * PreComputedWaves.TableSize;
                }
                else
                {
                    // Even in "evenly spaced detune" mode, random phase is still beneficial for lushness.
                    // If you later want deterministic / evenly spaced phase, change this to:
                    // phase = ((float)i / amount) * PreComputedWaves.TableSize;
                    phase = (float)rng.NextDouble() * PreComputedWaves.TableSize;
                }

                float phaseInc = (oscFreq * PreComputedWaves.TableSize) / sampleRate;

                for (int s = 0; s < sampleCount; s++)
                {
                    float v = PreComputedWaves.ReadLinear(wavetable, phase);

                    localL[s] = v * gainL;
                    localR[s] = v * gainR;

                    phase += phaseInc;

                    // Keep phase bounded to avoid float growth over long renders.
                    if (phase >= PreComputedWaves.TableSize)
                    {
                        phase -= PreComputedWaves.TableSize;
                        if (phase >= PreComputedWaves.TableSize)
                        {
                            phase = phase % PreComputedWaves.TableSize;
                        }
                    }
                }

                partialLeft[i] = localL;
                partialRight[i] = localR;
            });

        // Mixdown
        float[] mixedL = new float[sampleCount];
        float[] mixedR = new float[sampleCount];

        for (int i = 0; i < amount; i++)
        {
            float[] l = partialLeft[i];
            float[] r = partialRight[i];

            for (int s = 0; s < sampleCount; s++)
            {
                mixedL[s] += l[s];
                mixedR[s] += r[s];
            }
        }

        // Normalize to reduce clipping risk.
        // This is a simple scaling. Later you may prefer: peak normalize, limiter, or soft clip.
        float inv = 1.0f / amount;
        for (int s = 0; s < sampleCount; s++)
        {
            mixedL[s] *= inv;
            mixedR[s] *= inv;
        }

        return (new List<float>(mixedL), new List<float>(mixedR));
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