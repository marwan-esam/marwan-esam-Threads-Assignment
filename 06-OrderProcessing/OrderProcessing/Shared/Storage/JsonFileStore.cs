using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderProcessing.Shared.Exceptions;

namespace OrderProcessing.Shared.Storage;

public class JsonFileStore
{
  private static readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLock = new();
  private static readonly JsonSerializerOptions _options = new()
  {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters =  {new JsonStringEnumConverter()}
  };

  public static JsonSerializerOptions Options => _options;

  private static SemaphoreSlim GetLock(string fileName)
  {
    return _fileLock.GetOrAdd(fileName, _ => new SemaphoreSlim(1, 1));
  }
  public async Task<List<T>> ReadListAsync<T>(string fileName, CancellationToken ct = default) where T : class
  {
    var fileLock = GetLock(fileName);
    await fileLock.WaitAsync(ct);
    try
    {
      var list = await ReadCoreAsync<T>(fileName, ct);
      return list;
    } finally
    {
      fileLock.Release();
    }
  }

  private async Task<List<T>> ReadCoreAsync<T>(string fileName, CancellationToken ct)
  {
    try
    {
      string fullPath = Path.Combine(DataFiles.DataDir, fileName);
      if(!File.Exists(fullPath)) return [];
      string json = await File.ReadAllTextAsync(fullPath, Encoding.UTF8, ct);
      var list = JsonSerializer.Deserialize<List<T>>(json, _options);
      ArgumentNullException.ThrowIfNull(list);
      return list;  
    } catch(Exception e) when (e is JsonException || e is ArgumentNullException)
    {
      throw new DataFileCorruptedException(fileName);
    }
  }

  public List<T> ReadList<T>(string fileName) where T : class
  {
    var fileLock = GetLock(fileName);
    fileLock.Wait();
    try
    {
      var list = ReadCore<T>(fileName);
      return list;
    } finally
    {
      fileLock.Release();
    }
  }

  private List<T> ReadCore<T>(string fileName)
  {
    try
    {
      string fullPath = Path.Combine(DataFiles.DataDir, fileName);
      if(!File.Exists(fullPath)) return [];
      string json = File.ReadAllText(fullPath, Encoding.UTF8);
      var list = JsonSerializer.Deserialize<List<T>>(json.ToString(), _options);
      ArgumentNullException.ThrowIfNull(list);
      return list;  
    } catch(Exception e) when (e is JsonException || e is ArgumentNullException)
    {
      throw new DataFileCorruptedException(fileName);
    }
  }

  public async Task WriteListAsync<T>(string fileName, List<T> items, CancellationToken ct = default)
  {
    var fileLock = GetLock(fileName);
    await fileLock.WaitAsync(ct);
    try
    {
      await WriteCoreAsync<T>(fileName, items, ct);
    } finally
    {
      fileLock.Release();
    }
  }

  private async Task WriteCoreAsync<T>(string fileName, List<T> items, CancellationToken ct)
  {
    string json = JsonSerializer.Serialize<List<T>>(items, _options);
    string tempFileName = $"{fileName}.{Guid.NewGuid():N}.tmp";
    string fullPath = Path.Combine(DataFiles.DataDir, tempFileName);
    Directory.CreateDirectory(DataFiles.DataDir);
    await File.WriteAllTextAsync(fullPath, json, Encoding.UTF8, ct);
    File.Move(
      fullPath,
      Path.Combine(DataFiles.DataDir, fileName),
      overwrite: true
    );
  }

  public void WriteList<T>(string fileName, List<T> items) where T : class
  {
    var fileLock = GetLock(fileName);
    fileLock.Wait();
    try
    {
      WriteCore(fileName, items);
    } finally
    {
      fileLock.Release();
    }
  }

  private void WriteCore<T>(string fileName, List<T> items) where T : class
  {
    string json = JsonSerializer.Serialize<List<T>>(items, _options);
    string tempFileName = $"{fileName}.{Guid.NewGuid():N}.tmp";
    string fullPath = Path.Combine(DataFiles.DataDir, tempFileName);
    Directory.CreateDirectory(DataFiles.DataDir);
    File.WriteAllText(fullPath, json, Encoding.UTF8);
    File.Move(
      fullPath,
      Path.Combine(DataFiles.DataDir, fileName),
      overwrite: true
    );
  }

  public async Task<bool> UpdateListAsync<T>(string fileName, Func<List<T>,bool> updateAction, CancellationToken ct = default) where T : class
  {
    SemaphoreSlim fileLock = GetLock(fileName);
    await fileLock.WaitAsync(ct);
    try
    {
      var list = await ReadCoreAsync<T>(fileName, ct);
      if(!updateAction(list)) return false;
      await WriteCoreAsync(fileName, list, ct);
      return true;
    } finally
    {
      fileLock.Release();
    }
  }

  public bool UpdateList<T>(string fileName, Func<List<T>, bool> updateAction) where T : class
  {
    SemaphoreSlim fileLock = GetLock(fileName);
    fileLock.Wait();
    try
    {
      var list = ReadCore<T>(fileName);
      if(!updateAction(list)) return false;
      WriteCore<T>(fileName, list);
      return true;
    } finally
    {
      fileLock.Release();
    }
  }
}