namespace Centauri.Utils.Caching;

public class LRUCache<TKey, TValue>
{
    private readonly int _capacity;

    private readonly Dictionary<TKey,
        LinkedListNode<(TKey Key, TValue Value)>> _map;

    private readonly LinkedList<(TKey Key, TValue Value)> _list;

    public LRUCache(int capacity)
    {
        _capacity = capacity;
        _map = new Dictionary<TKey,
            LinkedListNode<(TKey, TValue)>>();
        _list = new LinkedList<(TKey, TValue)>();
    }

    public TValue? Get(TKey key)
    {
        if (!_map.TryGetValue(key, out var node))
            return default;

        _list.Remove(node);
        _list.AddFirst(node);

        return node.Value.Value;
    }

    public void Put(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out var existing))
        {
            _list.Remove(existing);
        }
        else if (_map.Count >= _capacity)
        {
            var last = _list.Last!;
            _map.Remove(last.Value.Key);
            _list.RemoveLast();
        }

        var node = new LinkedListNode<(TKey, TValue)>((key, value));
        _list.AddFirst(node);
        _map[key] = node;
    }
}