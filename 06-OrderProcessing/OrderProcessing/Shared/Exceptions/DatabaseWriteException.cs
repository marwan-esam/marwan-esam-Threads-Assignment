namespace OrderProcessing.Shared.Exceptions;

public class DatabaseWriteException : Exception
{
  public DatabaseWriteException(string message) : base(message) {}
}