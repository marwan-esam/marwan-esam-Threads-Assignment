using OrderProcessing.Phase1;
using OrderProcessing.Phase2;

namespace OrderProcessing;

public class Program
{
  public static async Task Main(string[] args)
  {
    Phase1Runner phase1 = new();
    Phase2Runner phase2 = new();
    // await phase1.RunAsync();
    await phase2.RunAsync();
  }
}
