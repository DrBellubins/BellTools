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
    }

    public static List<float> GenerateSawSample(int ammount, float detune, float lengthSeconds)
    {
        //
        return new List<float>();
    }

    public static void SaveSample(List<float> samples, string path)
    {
        
    }
}