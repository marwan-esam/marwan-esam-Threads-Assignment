using OrderProcessing.Phase1;
using OrderProcessing.Phase2;
using OrderProcessing.Phase3;

namespace OrderProcessing;

public class Program
{
  public static async Task Main(string[] args)
  {
    Phase3Runner phase3 = new();
    await phase3.RunAsync();
  }
}
