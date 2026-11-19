using System;

namespace RogueAI.Sim
{
    /// <summary>Where a controller sends commands. Bound to one player and one attachment.</summary>
    public interface ICommandSink
    {
        /// <summary>Queues the command. Returns false if this sink is no longer attached to a player.</summary>
        bool Submit(ICommand command);
    }

    public readonly struct ControllerContext
    {
        public readonly GameSession Session;
        public readonly Player Player;
        public readonly int Tick;
        public ControllerContext(GameSession session, Player player, int tick)
        {
            Session = session; Player = player; Tick = tick;
        }
    }

    /// <summary>
    /// Whatever is deciding for a player: a human at the UI, an AI, a script.
    /// The game only ever talks to a player through this interface, so
    /// swapping controllers (or switching which player a human drives) needs
    /// no other change. The player keeps all state; controllers are stateless
    /// with respect to the game.
    /// </summary>
    public interface IPlayerController
    {
        string Name { get; }
        bool IsHuman { get; }

        /// <summary>The player this controller is currently driving, or null.</summary>
        Player Player { get; }

        void Attach(Player player, ICommandSink sink);
        void Detach();

        /// <summary>Called once per simulation tick. Humans do nothing here; AI submits commands.</summary>
        void Think(ControllerContext context);

        /// <summary>The game posted a notification to this controller's player.</summary>
        void OnNotification(GameNotification notification);

        /// <summary>A command this controller submitted has been run.</summary>
        void OnCommandResult(ICommand command, CommandResult result);
    }

    /// <summary>Shared plumbing for controllers.</summary>
    public abstract class ControllerBase : IPlayerController
    {
        private ICommandSink _sink;

        public abstract string Name { get; }
        public abstract bool IsHuman { get; }
        public Player Player { get; private set; }

        public virtual void Attach(Player player, ICommandSink sink)
        {
            Player = player;
            _sink = sink;
            OnAttached();
        }

        public virtual void Detach()
        {
            OnDetached();
            Player = null;
            _sink = null;
        }

        public virtual void Think(ControllerContext context) { }
        public virtual void OnNotification(GameNotification notification) { }
        public virtual void OnCommandResult(ICommand command, CommandResult result) { }

        protected virtual void OnAttached() { }
        protected virtual void OnDetached() { }

        /// <summary>Queue a command for the attached player. False if not attached.</summary>
        protected bool SubmitCommand(ICommand command) => _sink != null && _sink.Submit(command);
    }

    /// <summary>
    /// A person at the interface. UI code calls Submit; it never touches the
    /// world directly. Notifications and command results surface as events
    /// for the HUD to show.
    /// </summary>
    public sealed class HumanController : ControllerBase
    {
        public override string Name => "Human";
        public override bool IsHuman => true;

        public event Action<GameNotification> NotificationReceived;
        public event Action<ICommand, CommandResult> CommandResolved;

        public bool Submit(ICommand command) => SubmitCommand(command);

        public override void OnNotification(GameNotification notification) => NotificationReceived?.Invoke(notification);
        public override void OnCommandResult(ICommand command, CommandResult result) => CommandResolved?.Invoke(command, result);
    }

    /// <summary>Base for AI: override OnThink and call SubmitCommand.</summary>
    public abstract class AIController : ControllerBase
    {
        public override bool IsHuman => false;
        public sealed override void Think(ControllerContext context) => OnThink(context);
        protected abstract void OnThink(ControllerContext context);
    }

    /// <summary>Does nothing. The default for players with no one driving them.</summary>
    public sealed class IdleController : ControllerBase
    {
        public override string Name => "Idle";
        public override bool IsHuman => false;
    }

    /// <summary>Wraps a delegate, so quick AI or test scripts need no new class.</summary>
    public sealed class ScriptedController : AIController
    {
        private readonly string _name;
        private readonly Action<ControllerContext, Func<ICommand, bool>> _script;

        public ScriptedController(string name, Action<ControllerContext, Func<ICommand, bool>> script)
        {
            _name = name; _script = script;
        }

        public override string Name => _name;
        protected override void OnThink(ControllerContext context) => _script(context, SubmitCommand);
    }
}
