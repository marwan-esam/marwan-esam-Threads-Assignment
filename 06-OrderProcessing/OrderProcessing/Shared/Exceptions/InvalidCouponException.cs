namespace OrderProcessing.Shared.Exceptions;

public class InvalidCouponException : Exception
{
  public InvalidCouponException(string message) : base(message) {} 
}