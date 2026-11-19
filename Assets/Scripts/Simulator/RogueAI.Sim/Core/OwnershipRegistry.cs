using System;
using System.Collections.Generic;

namespace RogueAI.Sim
{
    /// <summary>
    /// Single source of truth for who owns and who controls each entity.
    /// Owner is the legal holder; controller is who actually holds it
    /// (an occupier, or a player who compromised it). They start equal.
    /// Updates the owning Players' Owned / Controlled sets so both views agree.
    /// </summary>
    public sealed class OwnershipRegistry
    {
        private readonly Func<PlayerId, Player> _lookup;
        private readonly Dictionary<EntityId, PlayerId> _owner = new Dictionary<EntityId, PlayerId>();
        private readonly Dictionary<EntityId, PlayerId> _controller = new Dictionary<EntityId, PlayerId>();

        /// <summary>(entity, oldOwner, newOwner, oldController, newController)</summary>
        public event Action<EntityId, PlayerId, PlayerId, PlayerId, PlayerId> Changed;

        internal OwnershipRegistry(Func<PlayerId, Player> lookup) { _lookup = lookup; }

        public PlayerId GetOwner(EntityId e) => _owner.TryGetValue(e, out var p) ? p : PlayerId.None;

        /// <summary>The controller, which is the owner when no one else holds it.</summary>
        public PlayerId GetController(EntityId e) => _controller.TryGetValue(e, out var p) ? p : GetOwner(e);

        public bool IsOccupied(EntityId e)
        {
            PlayerId owner = GetOwner(e);
            return owner.IsValid && GetController(e) != owner;
        }

        /// <summary>
        /// Transfers ownership (annexation, purchase, creation). By default control
        /// moves to the new owner too, which ends any occupation.
        /// </summary>
        public void SetOwner(EntityId e, PlayerId newOwner, bool resetController = true)
        {
            PlayerId oldOwner = GetOwner(e), oldController = GetController(e);
            PlayerId newController = (resetController || !oldController.IsValid) ? newOwner : oldController;
            Apply(e, oldOwner, newOwner, oldController, newController);
        }

        /// <summary>Changes who controls an entity without changing who owns it (occupation, compromise).</summary>
        public void SetController(EntityId e, PlayerId newController)
        {
            PlayerId owner = GetOwner(e), oldController = GetController(e);
            Apply(e, owner, owner, oldController, newController);
        }

        /// <summary>Returns control to the owner.</summary>
        public void ClearControl(EntityId e) => SetController(e, GetOwner(e));

        private void Apply(EntityId e, PlayerId oldOwner, PlayerId newOwner, PlayerId oldController, PlayerId newController)
        {
            if (oldOwner == newOwner && oldController == newController) return;

            if (oldOwner != newOwner)
            {
                _lookup(oldOwner)?.RemoveOwned(e);
                if (newOwner.IsValid) { _owner[e] = newOwner; _lookup(newOwner)?.AddOwned(e); }
                else _owner.Remove(e);
            }

            if (oldController != newController)
            {
                _lookup(oldController)?.RemoveControlled(e);
                if (newController.IsValid) { _controller[e] = newController; _lookup(newController)?.AddControlled(e); }
                else _controller.Remove(e);
            }

            Changed?.Invoke(e, oldOwner, newOwner, oldController, newController);
        }
    }
}
