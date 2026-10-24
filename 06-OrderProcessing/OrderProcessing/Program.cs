using OrderProcessing.Phase1;
using OrderProcessing.Phase2;
using OrderProcessing.Phase3;
using OrderProcessing.Phase4;

namespace OrderProcessing;

public class Program
{
  public static async Task Main(string[] args)
  {
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (sender, eventArgs) =>
    {
      eventArgs.Cancel = true;
      cts.Cancel();
      Console.WriteLine("\n[Cancellation] Ctrl+C requested. Cancelling ongoing run...");
    };

    string? mode = null;
    int? cancelAfterMs = null;

    for (int i = 0; i < args.Length; i++)
    {
      if (args[i].Equals("--cancel-after", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
      {
        if (int.TryParse(args[i + 1], out int ms))
        {
          cancelAfterMs = ms;
        }
        i++;
      }
      else if (!args[i].StartsWith("--"))
      {
        mode = args[i].ToLowerInvariant();
      }
    }

    if (cancelAfterMs.HasValue)
    {
      cts.CancelAfter(cancelAfterMs.Value);
      Console.WriteLine($"[Cancellation] --cancel-after set to {cancelAfterMs.Value} ms");
    }

    if (string.IsNullOrEmpty(mode))
    {
      Console.WriteLine("==========================================================");
      Console.WriteLine("  SIMULATION ACADEMY — ORDER PROCESSING SYSTEM");
      Console.WriteLine("==========================================================");
      Console.WriteLine("1. Phase 1 — Async Data Access Layer & Validation");
      Console.WriteLine("2. Phase 2 — Concurrent Processing & Pricing");
      Console.WriteLine("3. Phase 3 — Full Checkout Workflow");
      Console.WriteLine("4. Phase 4 — Scale-Up & Modern Concurrency");
      Console.WriteLine("5. Run All Phases");
      Console.Write("\nSelect an option (1-5): ");

      var input = Console.ReadLine()?.Trim();
      mode = input switch
      {
        "1" => "phase1",
        "2" => "phase2",
        "3" => "phase3",
        "4" => "phase4",
        "5" => "all",
        _ => "phase4"
      };
    }

    try
    {
      switch (mode)
      {
        case "phase1":
          await new Phase1Runner().RunAsync(cts.Token);
          break;
        case "phase2":
          await new Phase2Runner().RunAsync(cts.Token);
          break;
        case "phase3":
          await new Phase3Runner().RunAsync(cts.Token);
          break;
        case "phase4":
          await new Phase4Runner().RunAsync(cts.Token);
          break;
        case "all":
          Console.WriteLine(">>> Running Phase 1...");
          await new Phase1Runner().RunAsync(cts.Token);
          Console.WriteLine("\n>>> Running Phase 2...");
          await new Phase2Runner().RunAsync(cts.Token);
          Console.WriteLine("\n>>> Running Phase 3...");
          await new Phase3Runner().RunAsync(cts.Token);
          Console.WriteLine("\n>>> Running Phase 4...");
          await new Phase4Runner().RunAsync(cts.Token);
          break;
        default:
          Console.WriteLine($"Unknown mode '{mode}'. Valid modes: phase1, phase2, phase3, phase4, all");
          break;
      }
    }
    catch (OperationCanceledException)
    {
      Console.WriteLine("\nRun cancelled — operation aborted via cancellation token.");
    }
  }
}
