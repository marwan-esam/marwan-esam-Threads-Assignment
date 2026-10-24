namespace OrderProcessing.Shared.Logging;

public static class Log
{
  private static readonly Lock _lock = new();
  private static readonly string _datePattern = "HH:mm:ss.fff";
  public static void Info(string message)
  {
    lock(_lock)
    {
      System.Console.WriteLine($"[{DateTime.Now.ToString(_datePattern)}] [Thread {Thread.CurrentThread.ManagedThreadId}] {message}"); 
    }
  }

  public static void Success(string message)
  {
    lock(_lock) {
      Console.ForegroundColor = ConsoleColor.Green;
      System.Console.WriteLine($"[{DateTime.Now.ToString(_datePattern)}] [Thread {Thread.CurrentThread.ManagedThreadId}] {message}");
      Console.ResetColor();
    }
  }

  public static void Error(string message)
  {
    lock(_lock)
    {
      Console.ForegroundColor = ConsoleColor.Red;
      System.Console.WriteLine($"[{DateTime.Now.ToString(_datePattern)}] [Thread {Thread.CurrentThread.ManagedThreadId}] {message}"); 
      Console.ResetColor();
    }
  }
}