using System;

namespace RogueAI.Sim
{
    /// <summary>Identifies a player (actor). 0 is "none".</summary>
    public readonly struct PlayerId : IEquatable<PlayerId>
    {
        public static readonly PlayerId None = new PlayerId(0);

        public readonly int Value;
        public PlayerId(int value) { Value = value; }

        public bool IsValid => Value > 0;
        public bool Equals(PlayerId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is PlayerId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => "P" + Value;

        public static bool operator ==(PlayerId a, PlayerId b) => a.Value == b.Value;
        public static bool operator !=(PlayerId a, PlayerId b) => a.Value != b.Value;
    }

    /// <summary>The kinds of thing a player can own or control.</summary>
    public enum EntityKind : byte
    {
        None = 0,
        Region = 1,
        Organization = 2,
        Asset = 3,
        Person = 4,
        Unit = 5,
    }

    /// <summary>
    /// Identifies any simulation entity. For regions, Value is the state id
    /// from the map's ID texture.
    /// </summary>
    public readonly struct EntityId : IEquatable<EntityId>
    {
        public readonly EntityKind Kind;
        public readonly int Value;
        public EntityId(EntityKind kind, int value) { Kind = kind; Value = value; }

        public bool IsValid => Kind != EntityKind.None;
        public bool Equals(EntityId other) => Kind == other.Kind && Value == other.Value;
        public override bool Equals(object obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ Value;
        public override string ToString() => Kind + ":" + Value;

        public static bool operator ==(EntityId a, EntityId b) => a.Equals(b);
        public static bool operator !=(EntityId a, EntityId b) => !a.Equals(b);

        public static EntityId Region(int stateId) => new EntityId(EntityKind.Region, stateId);
        public static EntityId Organization(int id) => new EntityId(EntityKind.Organization, id);
        public static EntityId Asset(int id) => new EntityId(EntityKind.Asset, id);
    }

    /// <summary>The four teams from the design doc. A player belongs to one.</summary>
    public enum Faction
    {
        Red,
        Blue,
        White,
        Gray,
    }
}
