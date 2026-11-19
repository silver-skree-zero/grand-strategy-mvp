using System.Collections.Generic;

namespace RogueAI.Sim
{
    /// <summary>
    /// A participant in the simulation: Red, a Blue faction, a White faction,
    /// a Gray actor. Holds everything that belongs to the participant
    /// (resources, stats, what it owns and controls, what it knows, its
    /// inbox). It does not decide anything; its Controller does.
    /// </summary>
    public sealed class Player
    {
        private readonly HashSet<EntityId> _owned = new HashSet<EntityId>();
        private readonly HashSet<EntityId> _controlled = new HashSet<EntityId>();
        private readonly List<GameNotification> _inbox = new List<GameNotification>();

        public PlayerId Id { get; }
        public string Name { get; set; }
        public Faction Faction { get; }

        /// <summary>Money, compute capacity, and other things the player has.</summary>
        public StatBlock Resources { get; } = new StatBlock();

        /// <summary>Detection, investigation, narrative share and similar scores.</summary>
        public StatBlock Stats { get; } = new StatBlock();

        /// <summary>What this player knows. UI renders from this, never from true state.</summary>
        public KnowledgeStore Knowledge { get; } = new KnowledgeStore();

        /// <summary>Entities this player legally owns.</summary>
        public IReadOnlyCollection<EntityId> Owned => _owned;

        /// <summary>Entities this player actually controls (may differ from owned: occupation, compromise).</summary>
        public IReadOnlyCollection<EntityId> Controlled => _controlled;

        public IReadOnlyList<GameNotification> Inbox => _inbox;

        /// <summary>Whoever is currently deciding for this player. Never null once added to a session.</summary>
        public IPlayerController Controller { get; internal set; }

        internal ControllerLink Link { get; set; }

        internal Player(PlayerId id, string name, Faction faction)
        {
            Id = id; Name = name; Faction = faction;
        }

        // Maintained by OwnershipRegistry so the sets can't drift from the registry.
        internal void AddOwned(EntityId e) => _owned.Add(e);
        internal void RemoveOwned(EntityId e) => _owned.Remove(e);
        internal void AddControlled(EntityId e) => _controlled.Add(e);
        internal void RemoveControlled(EntityId e) => _controlled.Remove(e);
        internal void AddNotification(GameNotification n) => _inbox.Add(n);

        public override string ToString() => Name + " (" + Faction + ")";
    }

    /// <summary>
    /// The sink a controller receives. Detaching a controller deactivates its
    /// link, so a stale reference (an old UI panel, a leftover AI callback)
    /// cannot keep issuing commands after a swap.
    /// </summary>
    internal sealed class ControllerLink : ICommandSink
    {
        private readonly CommandBus _bus;
        private readonly Player _player;
        private readonly IPlayerController _controller;
        public bool Active = true;

        public ControllerLink(CommandBus bus, Player player, IPlayerController controller)
        {
            _bus = bus; _player = player; _controller = controller;
        }

        public bool Submit(ICommand command)
        {
            if (!Active || command == null) return false;
            _bus.Submit(_player, command, _controller);
            return true;
        }
    }
}
