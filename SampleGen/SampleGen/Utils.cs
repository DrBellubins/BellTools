namespace SampleGen;

public class Utils
{
    /// <summary>
    /// Generates 1D Perlin noise at the given coordinate (phase).
    /// </summary>
    /// <param name="phase">Input coordinate in continuous space.</param>
    /// <param name="seed">Deterministic seed for the noise field.</param>
    /// <returns>
    /// A noise value approximately in [-1, 1]. (Exact extrema depend on interpolation and gradients.)
    /// </returns>
    public static float Perlin1DNoise(float phase, int seed = 0)
    {
        // Identify the lattice points around phase.
        int x0 = (int)System.MathF.Floor(phase);
        int x1 = x0 + 1;
    
        // Local coordinate within the cell.
        float t = phase - x0; // in [0, 1)
    
        // Gradients at lattice points.
        float g0 = Gradient(x0, seed);
        float g1 = Gradient(x1, seed);
    
        // Distance vectors in 1D.
        float d0 = t;         // phase - x0
        float d1 = t - 1.0f;  // phase - x1
    
        // Dot products (in 1D this is simply gradient * distance).
        float v0 = g0 * d0;
        float v1 = g1 * d1;
    
        // Smooth interpolation.
        float u = Fade(t);
        return Lerp(v0, v1, u);
    }
    
    // Fade curve used by classic Perlin noise.
    private static float Fade(float t)
    {
        // 6t^5 - 15t^4 + 10t^3
        return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
    }
    
    private static float Lerp(float a, float b, float t)
    {
        return a + ((b - a) * t);
    }
    
    // 1D gradient function. The gradient at each integer lattice point is either +1 or -1.
    // The sign is derived deterministically from the integer coordinate and the seed.
    private static float Gradient(int x, int seed)
    {
        unchecked
        {
            uint h = (uint)x;
            h ^= (uint)seed;
            h *= 0x27D4EB2Du;
            h ^= h >> 15;
    
            // Lowest bit chooses the gradient direction.
            return ((h & 1u) == 0u) ? 1.0f : -1.0f;
        }
    }
}