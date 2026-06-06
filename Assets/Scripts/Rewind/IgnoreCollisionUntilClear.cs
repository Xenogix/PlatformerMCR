using System.Collections.Generic;
using UnityEngine;

// Ignores collision between this object's collider and a set of peers it spawned overlapping,
// re-enabling each pair the moment they separate. This avoids the violent depenetration "pop"
// when an echo is spawned on top of the live player (or another echo) at a rewind: collision
// between them only begins once one walks clear of the other. Self-destructs once all clear.
[RequireComponent(typeof(Collider2D))]
public sealed class IgnoreCollisionUntilClear : MonoBehaviour
{
    private Collider2D _self;
    private readonly List<Collider2D> _ignored = new();

    private void Awake() => _self = GetComponent<Collider2D>();

    public void IgnoreWhileOverlapping(IEnumerable<Collider2D> peers)
    {
        if (_self == null) _self = GetComponent<Collider2D>();
        foreach (var peer in peers)
        {
            if (peer == null || peer == _self) continue;
            if (_self.Distance(peer).isOverlapped)
            {
                Physics2D.IgnoreCollision(_self, peer, true);
                _ignored.Add(peer);
            }
        }
        if (_ignored.Count == 0) Destroy(this);
    }

    private void FixedUpdate()
    {
        for (int i = _ignored.Count - 1; i >= 0; i--)
        {
            Collider2D peer = _ignored[i];
            if (peer == null || !_self.Distance(peer).isOverlapped)
            {
                if (peer != null) Physics2D.IgnoreCollision(_self, peer, false);
                _ignored.RemoveAt(i);
            }
        }
        if (_ignored.Count == 0) Destroy(this);
    }
}
