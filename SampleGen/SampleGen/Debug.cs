namespace SampleGen;

public class Debug
{
    public static void Log(string message)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(message);
        Console.ForegroundColor = ConsoleColor.White;
    }
    
    public static void Warning(string warning)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(warning);
        Console.ForegroundColor = ConsoleColor.White;
    }
    
    public static void Error(string error)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(error);
        Console.ForegroundColor = ConsoleColor.White;
    }
}