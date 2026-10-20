using OrderProcessing.Phase1;

namespace OrderProcessing;

public class Program
{
  public static async Task Main(string[] args)
  {
    Phase1Runner phase1 = new();
    await phase1.RunAsync();
  }
}
