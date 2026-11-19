using System;
using System.Collections.Generic;

namespace RogueAI.Sim
{
    /// <summary>
    /// A fact as one player knows it: the value, how sure they are, where it
    /// came from, and when it was learned. Reality lives in the simulation;
    /// a player only ever sees Known facts.
    /// </summary>
    public readonly struct Known<T>
    {
        public readonly T Value;
        public readonly float Confidence;   // 0..1
        public readonly string Source;      // free text for now ("own operation", "ping", ...)
        public readonly int AsOfTick;

        public Known(T value, float confidence, string source, int asOfTick)
        {
            Value = value; Confidence = confidence; Source = source; AsOfTick = asOfTick;
        }
    }

    /// <summary>
    /// Per-player store of what that player knows, keyed by (entity, fact name).
    /// Minimal on purpose: heat-map pings, dossiers and estimates will be
    /// built on this later.
    /// </summary>
    public sealed class KnowledgeStore
    {
        private readonly struct Key : IEquatable<Key>
        {
            public readonly EntityId Entity;
            public readonly string Fact;
            public Key(EntityId entity, string fact) { Entity = entity; Fact = fact; }
            public bool Equals(Key other) => Entity.Equals(other.Entity) && Fact == other.Fact;
            public override bool Equals(object obj) => obj is Key other && Equals(other);
            public override int GetHashCode() => Entity.GetHashCode() * 31 + (Fact != null ? Fact.GetHashCode() : 0);
        }

        private readonly Dictionary<Key, object> _facts = new Dictionary<Key, object>();

        /// <summary>(entity, factName)</summary>
        public event Action<EntityId, string> Changed;

        public void Set<T>(EntityId entity, string fact, Known<T> known)
        {
            _facts[new Key(entity, fact)] = known;
            Changed?.Invoke(entity, fact);
        }

        public bool TryGet<T>(EntityId entity, string fact, out Known<T> known)
        {
            if (_facts.TryGetValue(new Key(entity, fact), out object boxed) && boxed is Known<T> typed)
            {
                known = typed;
                return true;
            }
            known = default;
            return false;
        }

        public bool Forget(EntityId entity, string fact)
        {
            bool removed = _facts.Remove(new Key(entity, fact));
            if (removed) Changed?.Invoke(entity, fact);
            return removed;
        }
    }
}
