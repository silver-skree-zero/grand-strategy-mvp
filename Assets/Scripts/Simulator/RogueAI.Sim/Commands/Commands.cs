using System;
using System.Collections.Generic;

namespace RogueAI.Sim
{
    /// <summary>
    /// Marker for anything a controller asks its player to do. Commands are
    /// plain data; the logic lives in handlers registered on the CommandBus,
    /// so each module adds its own commands without touching this one.
    /// </summary>
    public interface ICommand { }

    public readonly struct CommandResult
    {
        public readonly bool Ok;
        public readonly string Reason;
        private CommandResult(bool ok, string reason) { Ok = ok; Reason = reason; }

        public static readonly CommandResult Success = new CommandResult(true, null);
        public static CommandResult Fail(string reason) => new CommandResult(false, reason);
    }

    public sealed class CommandEnvelope
    {
        public Player Issuer { get; internal set; }
        public IPlayerController Source { get; internal set; }
        public ICommand Command { get; internal set; }
        public int Tick { get; internal set; }
    }

    /// <summary>
    /// Queues commands from controllers and runs them through registered
    /// handlers in submission order. Human and AI commands take the same path.
    /// </summary>
    public sealed class CommandBus
    {
        private readonly GameSession _session;
        private readonly Dictionary<Type, Func<GameSession, Player, ICommand, CommandResult>> _handlers
            = new Dictionary<Type, Func<GameSession, Player, ICommand, CommandResult>>();
        private readonly Queue<CommandEnvelope> _queue = new Queue<CommandEnvelope>();

        /// <summary>Raised after every command runs. Use for logging, replays, UI errors.</summary>
        public event Action<CommandEnvelope, CommandResult> Executed;

        internal CommandBus(GameSession session) { _session = session; }

        /// <summary>
        /// Register the handler for a command type. The handler validates and
        /// applies the command for the issuing player and returns the result.
        /// </summary>
        public void Register<T>(Func<GameSession, Player, T, CommandResult> handler) where T : ICommand
        {
            _handlers[typeof(T)] = (session, player, command) => handler(session, player, (T)command);
        }

        internal void Submit(Player issuer, ICommand command, IPlayerController source)
        {
            if (issuer == null || command == null) return;
            _queue.Enqueue(new CommandEnvelope
            {
                Issuer = issuer, Source = source, Command = command, Tick = _session.Tick,
            });
        }

        /// <summary>Runs everything queued so far. Commands queued while running wait for the next call.</summary>
        internal void Process()
        {
            int count = _queue.Count;
            for (int i = 0; i < count; i++)
            {
                var env = _queue.Dequeue();
                CommandResult result;
                if (!_handlers.TryGetValue(env.Command.GetType(), out var handler))
                {
                    result = CommandResult.Fail("No handler registered for " + env.Command.GetType().Name);
                }
                else
                {
                    try { result = handler(_session, env.Issuer, env.Command); }
                    catch (Exception e) { result = CommandResult.Fail("Command threw: " + e.Message); }
                }

                try { env.Source?.OnCommandResult(env.Command, result); } catch (Exception) { }
                Executed?.Invoke(env, result);
            }
        }
    }

    // ---- Built-in commands ---------------------------------------------------

    /// <summary>Debug: set one of the issuer's resources.</summary>
    public sealed class DebugSetResourceCommand : ICommand
    {
        public string StatId;
        public float Value;
    }

    /// <summary>Mark an inbox notification as handled.</summary>
    public sealed class AcknowledgeNotificationCommand : ICommand
    {
        public int NotificationId;
    }

    internal static class CoreCommands
    {
        public static void Register(CommandBus bus)
        {
            bus.Register<DebugSetResourceCommand>((session, player, cmd) =>
            {
                if (string.IsNullOrEmpty(cmd.StatId)) return CommandResult.Fail("Missing stat id");
                player.Resources.Set(cmd.StatId, cmd.Value);
                return CommandResult.Success;
            });

            bus.Register<AcknowledgeNotificationCommand>((session, player, cmd) =>
            {
                foreach (var n in player.Inbox)
                {
                    if (n.Id != cmd.NotificationId) continue;
                    n.Resolved = true;
                    return CommandResult.Success;
                }
                return CommandResult.Fail("No such notification");
            });
        }
    }
}
