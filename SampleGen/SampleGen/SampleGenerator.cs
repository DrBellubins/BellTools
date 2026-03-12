using System.Diagnostics;

namespace SampleGen;

public class SampleGenerator
{
    public static string SamplePath = Path.Combine(AppContext.BaseDirectory, "Samples");

    private static Stopwatch timer = new();
    
    public static void Initialize(string[] args)
    {
        /*if (string.IsNullOrEmpty(args[0]))
        {
            Debug.Error("No arguments given!");
            return;
        }*/

        if (!Directory.Exists(SamplePath))
            Directory.CreateDirectory(SamplePath);
        
        PreComputedWaves.Initialize();
        
        Run();
    }

    public static void Run()
    {
        timer.Restart();

        //int amount = 16384;
        int amount = 256;
        
        var sample = GenerateSample(SampleType.Saw, 440f / 12f, 44100, amount,
            10f, 0.006f, true);
        
        timer.Stop();
        
        Debug.Log($"Finished generation! Elapsed: {timer.ElapsedMilliseconds}ms");

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
        SaveSample(sample, Path.Combine(SamplePath, $"{sample.Type}-{sample.LengthSeconds}-{timestamp}"));
    }

    public static Sample GenerateOctaveSample(
        SampleType type,
        float frequency,
        int octaves,
        int sampleRate,
        int amount,
        float lengthSeconds,
        float detune,
        bool randomDetuneDistro = false)
    {
        Sample sample = new Sample();

        for (int i = 0; i < octaves; i++)
        {
            var outputSample = GenerateSample(type, frequency, sampleRate, amount,
                lengthSeconds, detune, randomDetuneDistro);
        }
        
        return sample;
    }
    
    // Returns left and right channel samples
    public static Sample GenerateSample(
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
            Debug.Error("[GenerateSample] Sample rate must be positive.");
            return new Sample();
        }

        if (lengthSeconds <= 0.0f)
        {
            Debug.Error("[GenerateSample] Length in seconds must be positive.");
            return new Sample();
        }

        if (amount <= 0)
        {
            Debug.Error("[GenerateSample] Amount must be positive.");
            return new Sample();
        }

        int sampleCount = (int)MathF.Round(sampleRate * lengthSeconds);
        
        if (sampleCount <= 0)
            return new Sample();

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
                int seed = unchecked((int)0x6D2B79F5 ^ (w * (int)0x85EBCA6B));
                Random rng = new Random(seed);

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

        // Peak-normalize to -1 dBFS:
        NormalizePeakToDb(ref mixedL, ref mixedR, -1.0f);

        var sample = new Sample();
        sample.Type = type;
        sample.SampleRate = sampleRate;
        sample.Frequency = frequency;
        sample.Detune = detune;
        sample.LengthSeconds = lengthSeconds;
        sample.LeftSamples = new List<float>(mixedL);
        sample.RightSamples = new List<float>(mixedR);

        return sample;
    }

    public static void SaveSample(Sample sample, string path)
    {
        if (sample.LeftSamples == null || sample.RightSamples == null)
            Debug.Error("[SaveSample] Sample channels must not be null.");

        if (sample.LeftSamples.Count != sample.RightSamples.Count)
            Debug.Error("[SaveSample] LeftSamples and RightSamples must have identical lengths.");

        if (sample.SampleRate <= 0)
            Debug.Error("[SaveSample] SampleRate must be a positive integer.");

        string? dir = Path.GetDirectoryName(path);
        
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        const short audioFormatIeeeFloat = 3;
        const short numChannels = 2;
        const short bitsPerSample = 32;
        const short blockAlign = (short)(numChannels * (bitsPerSample / 8));
        int byteRate = sample.SampleRate * blockAlign;

        int frames = sample.LeftSamples.Count;
        int dataSize = frames * blockAlign;

        // RIFF chunk size excludes the first 8 bytes ("RIFF" + uint32 size).
        int riffChunkSize = 4 /*WAVE*/ + (8 + 16) /*fmt*/ + (8 + dataSize) /*data*/;

        using FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using BinaryWriter bw = new BinaryWriter(fs);

        // RIFF header
        bw.Write("RIFF"u8.ToArray());
        bw.Write(riffChunkSize);
        bw.Write("WAVE"u8.ToArray());

        // fmt chunk (PCM-style header, but with format tag 3 for IEEE float)
        bw.Write("fmt "u8.ToArray());
        bw.Write(16); // PCM/IEEE float fmt chunk size
        bw.Write(audioFormatIeeeFloat);
        bw.Write(numChannels);
        bw.Write(sample.SampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write(bitsPerSample);

        // data chunk
        bw.Write("data"u8.ToArray());
        bw.Write(dataSize);

        for (int i = 0; i < frames; i++)
        {
            float l = SanitizeFloatSample(sample.LeftSamples[i]);
            float r = SanitizeFloatSample(sample.RightSamples[i]);

            bw.Write(l);
            bw.Write(r);
        }
        
        Debug.Log($"Sample saved! '{path}', SampleRate: {sample.SampleRate}, Length: {sample.LengthSeconds}");
    }

    private static float SanitizeFloatSample(float v)
    {
        if (float.IsNaN(v) || float.IsInfinity(v))
            return 0.0f;

        // Optional safety clamp. If you want to preserve values outside [-1,1], remove this.
        if (v > 1.0f)
            return 1.0f;

        if (v < -1.0f)
            return -1.0f;

        return v;
    }
    
    private static float DbToLinear(float db)
    {
        return MathF.Pow(10.0f, db / 20.0f);
    }

    private static void NormalizePeakToDb(ref float[] left, ref float[] right, float targetDb)
    {
        if (left.Length != right.Length)
            Debug.Error("[NormalizePeakToDb] Left and right buffers must have identical lengths.");

        float peak = 0.0f;

        for (int i = 0; i < left.Length; i++)
        {
            float aL = MathF.Abs(left[i]);
            float aR = MathF.Abs(right[i]);

            if (aL > peak)
                peak = aL;

            if (aR > peak)
                peak = aR;
        }

        if (peak <= 0.0f)
            return;

        float targetPeak = DbToLinear(targetDb); // e.g. -1 dB -> 0.8912509...
        float gain = targetPeak / peak;

        for (int i = 0; i < left.Length; i++)
        {
            left[i] *= gain;
            right[i] *= gain;
        }
    }
}

public struct Sample()
{
    public SampleType Type;
    public int SampleRate;
    public float Frequency;
    public float Detune;
    public float LengthSeconds;
    public List<float> LeftSamples;
    public List<float> RightSamples;
}

public enum SampleType
{
    Sine,
    Saw,
    Square,
    Triangle
}