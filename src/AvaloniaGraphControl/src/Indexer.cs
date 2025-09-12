
using System;

namespace AvaloniaGraphControl
{
  public class Indexer<K, T>
  {
    public Indexer(Func<K, T> getValue, Action<K, T> setValue)
    {
      GetValue = getValue;
      SetValue = setValue;
    }
    public T this[K key]
    {
      get => GetValue(key);
      set => SetValue(key, value);
    }

    private readonly Func<K, T> GetValue;
    private readonly Action<K, T> SetValue;
  }
}