using System;
using System.Collections.Generic;

namespace RogueAI.Sim
{
    /// <summary>
    /// String-keyed numeric values with change notification. Players keep two
    /// of these (Resources and Stats). Keys are plain strings so new
    /// resources and stats are data, not new fields.
    /// </summary>
    public sealed class StatBlock
    {
        private readonly Dictionary<string, float> _values = new Dictionary<string, float>();

        /// <summary>(statId, oldValue, newValue)</summary>
        public event Action<string, float, float> Changed;

        public bool Has(string id) => _values.ContainsKey(id);

        public float Get(string id, float fallback = 0f)
            => _values.TryGetValue(id, out float v) ? v : fallback;

        public void Set(string id, float value)
        {
            float old = Get(id);
            bool existed = _values.ContainsKey(id);
            _values[id] = value;
            if (!existed || old != value) Changed?.Invoke(id, old, value);
        }

        public void Add(string id, float delta) => Set(id, Get(id) + delta);

        public IEnumerable<KeyValuePair<string, float>> All => _values;
    }

    /// <summary>
    /// Well-known stat keys. Not exhaustive: any string works as a key.
    /// Compute is a capacity that multiplies what a player can do, never a
    /// currency spent per action.
    /// </summary>
    public static class StatIds
    {
        // Resources
        public const string Money = "money";
        public const string Compute = "compute";

        // Stats
        public const string Detection = "detection";
        public const string Investigation = "investigation";
        public const string NarrativeShare = "narrative_share";
        public const string Legitimacy = "legitimacy";
    }
}
