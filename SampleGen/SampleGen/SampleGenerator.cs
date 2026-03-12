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

        // Final mix buffers
        float[] mixedL = new float[sampleCount];
        float[] mixedR = new float[sampleCount];

        // Choose worker count automatically.
        // For very large oscillator counts this is appropriate; for tiny counts we clamp.
        int workerCount = Math.Min(Math.Max(1, Environment.ProcessorCount), amount);

        // Thread-local accumulation buffers: O(workerCount * sampleCount) memory.
        float[][] workerSumL = new float[workerCount][];
        float[][] workerSumR = new float[workerCount][];

        for (int w = 0; w < workerCount; w++)
        {
            workerSumL[w] = new float[sampleCount];
            workerSumR[w] = new float[sampleCount];
        }

        // Partition oscillators across workers: [start, end)
        int baseCount = amount / workerCount;
        int remainder = amount % workerCount;

        Parallel.For(
            fromInclusive: 0,
            toExclusive: workerCount,
            body: w =>
            {
                int start = w * baseCount + Math.Min(w, remainder);
                int count = baseCount + (w < remainder ? 1 : 0);
                int end = start + count;

                float[] sumL = workerSumL[w];
                float[] sumR = workerSumR[w];

                // Deterministic per-worker RNG; oscillator-level randomness is derived from it.
                // This avoids sharing RNGs between threads.
                Random rng = new Random(unchecked(0x6D2B79F5 ^ (w * 0x85EBCA6B)));

                for (int i = start; i < end; i++)
                {
                    // DETUNE (fractional frequency)
                    // detuneFactor in [-detune, +detune], applied as oscFreq = frequency * (1 + detuneFactor)
                    float detuneFactor = 0.0f;

                    if (amount > 1 && detune != 0.0f)
                    {
                        if (randomDetuneDistro)
                        {
                            float u = (float)rng.NextDouble();           // [0, 1)
                            detuneFactor = ((u * 2.0f) - 1.0f) * detune; // [-detune, +detune]
                        }
                        else
                        {
                            float t = (float)i / (amount - 1);           // [0, 1]
                            detuneFactor = ((t * 2.0f) - 1.0f) * detune; // [-detune, +detune]
                        }
                    }

                    float oscFreq = frequency * (1.0f + detuneFactor);
                    float phaseInc = (oscFreq * PreComputedWaves.TableSize) / sampleRate;

                    // RANDOM START PHASE (per oscillator)
                    float phase = (float)rng.NextDouble() * PreComputedWaves.TableSize;

                    // STEREO: constant-power pan spread across ensemble
                    // If you later want *random* panning when randomDetuneDistro is true, we can add it.
                    float pan = 0.0f;
                    if (amount > 1)
                    {
                        float tPan = (float)i / (amount - 1); // [0, 1]
                        pan = (tPan * 2.0f) - 1.0f;           // [-1, 1]
                    }

                    float angle = (pan + 1.0f) * 0.25f * MathF.PI; // [-1,1] -> [0, pi/2]
                    float gainL = MathF.Cos(angle);
                    float gainR = MathF.Sin(angle);

                    // Generate and accumulate directly into the worker buffers
                    for (int s = 0; s < sampleCount; s++)
                    {
                        float v = PreComputedWaves.ReadLinear(wavetable, phase);

                        sumL[s] += v * gainL;
                        sumR[s] += v * gainR;

                        phase += phaseInc;

                        // Keep phase bounded (cheap and sufficient)
                        if (phase >= PreComputedWaves.TableSize)
                        {
                            phase -= PreComputedWaves.TableSize;

                            if (phase >= PreComputedWaves.TableSize)
                            {
                                phase = phase % PreComputedWaves.TableSize;
                            }
                        }
                    }
                }
            });

        // Reduce worker buffers into final buffers
        for (int w = 0; w < workerCount; w++)
        {
            float[] sumL = workerSumL[w];
            float[] sumR = workerSumR[w];

            for (int s = 0; s < sampleCount; s++)
            {
                mixedL[s] += sumL[s];
                mixedR[s] += sumR[s];
            }
        }

        // Normalize by oscillator count to manage amplitude.
        // This is intentionally simple; later you can add peak normalization/limiting.
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