using System;
using System.Collections.Generic;
using System.Threading;

namespace ModelDownloader.Models;

public class LruCache<TKey, TValue> where TKey : notnull
{
    private class CacheItem
    {
        public TKey Key { get; }
        public TValue Value { get; }

        public CacheItem(TKey key, TValue value)
        {
            Key = key;
            Value = value;
        }
    }

    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<CacheItem>> _cacheMap;
    private readonly LinkedList<CacheItem> _lruList;
    private readonly Lock _lock = new();

    public LruCache(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
            
        _capacity = capacity;
        _cacheMap = new Dictionary<TKey, LinkedListNode<CacheItem>>(capacity);
        _lruList = new LinkedList<CacheItem>();
    }

    public bool TryGetValue(TKey key, out TValue? value)
    {
        lock (_lock)
        {
            if (_cacheMap.TryGetValue(key, out var node))
            {
                value = node.Value.Value;
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                return true;
            }
        }

        value = default;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_cacheMap.TryGetValue(key, out var node))
            {
                _lruList.Remove(node);
                _cacheMap.Remove(key);
                DisposeValue(node.Value.Value);
            }

            var cacheItem = new CacheItem(key, value);
            var newNode = new LinkedListNode<CacheItem>(cacheItem);
            _lruList.AddFirst(newNode);
            _cacheMap[key] = newNode;

            if (_cacheMap.Count > _capacity)
            {
                var lastNode = _lruList.Last;
                if (lastNode != null)
                {
                    _cacheMap.Remove(lastNode.Value.Key);
                    _lruList.RemoveLast();
                    DisposeValue(lastNode.Value.Value);
                }
            }
        }
    }

    private void DisposeValue(TValue value)
    {
        if (value is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
