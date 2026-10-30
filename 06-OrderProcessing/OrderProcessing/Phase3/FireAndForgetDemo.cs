using OrderProcessing.Phase1;
using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase3;

public class FireAndForgetDemo
{
  private readonly OrderRepository _orderRepository;
  private readonly IEmailService _emailService;

  public FireAndForgetDemo(OrderRepository orderRepository, IEmailService emailService)
  {
    _orderRepository = orderRepository;
    _emailService = emailService;
  }

  public async Task ProcessOrderBrokenAsync(Order order, Customer customer)
  {
    await _orderRepository.SaveAsync(order);
#pragma warning disable CS4014
    _emailService.SendConfirmationAsync(order, customer);
#pragma warning restore CS4014
    Console.WriteLine("[FireAndForgetDemo] Order processing finished (Broken: email was not awaited)");
  }

  public async Task ProcessOrderFixedAsync(Order order, Customer customer)
  {
    await _orderRepository.SaveAsync(order);
    await _emailService.SendConfirmationAsync(order, customer);
    Console.WriteLine("[FireAndForgetDemo] Order processing finished (Fixed: email awaited)");
  }
}
