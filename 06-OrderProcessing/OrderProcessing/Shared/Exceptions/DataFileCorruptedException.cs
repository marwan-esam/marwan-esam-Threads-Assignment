namespace OrderProcessing.Shared.Exceptions;

public class DataFileCorruptedException : Exception
{
  public DataFileCorruptedException(string fileName) : base($"Data file '{fileName}' is corrupted") {}

}