using System;
using System.Collections.Generic;

namespace RogueAI.Sim
{
    /// <summary>
    /// The root of the simulation: players, ownership, the command bus, the
    /// tick, and who the UI is currently viewing as. Pure C#; Unity code
    /// holds one of these and drives it.
    ///
    /// Per tick (Advance): every player's controller thinks (AI submits
    /// commands), then all queued commands run in submission order. Human
    /// commands arrive from the UI between ticks and are included. Call
    /// ProcessCommands every frame so a human can give orders while paused.
    /// </summary>
    public sealed class GameSession
    {
        private readonly List<Player> _players = new List<Player>();
        private readonly Dictionary<int, Player> _byId = new Dictionary<int, Player>();
        private int _nextPlayerId = 1;
        private int _nextNotificationId = 1;

        public int Tick { get; private set; }
        public CommandBus Commands { get; }
        public OwnershipRegistry Ownership { get; }
        public IReadOnlyList<Player> Players => _players;

        /// <summary>
        /// The player whose knowledge the UI shows. Changing it is how a human
        /// tags between players. It is independent of who controls whom.
        /// </summary>
        public Player Viewer { get; private set; }

        /// <summary>(previous, current)</summary>
        public event Action<Player, Player> ViewerChanged;

        /// <summary>(player, oldController, newController)</summary>
        public event Action<Player, IPlayerController, IPlayerController> ControllerChanged;

        public event Action<Player, GameNotification> NotificationPosted;
        public event Action<Player> PlayerAdded;

        public GameSession()
        {
            Commands = new CommandBus(this);
            Ownership = new OwnershipRegistry(GetPlayer);
            CoreCommands.Register(Commands);
        }

        // ---- Players ---------------------------------------------------------

        public Player AddPlayer(string name, Faction faction, IPlayerController controller = null)
        {
            var player = new Player(new PlayerId(_nextPlayerId++), name, faction);
            _players.Add(player);
            _byId[player.Id.Value] = player;
            AttachController(player, controller ?? new IdleController());
            if (Viewer == null) SetViewer(player);
            PlayerAdded?.Invoke(player);
            return player;
        }

        /// <summary>Returns null for an unknown or empty id.</summary>
        public Player GetPlayer(PlayerId id) => _byId.TryGetValue(id.Value, out var p) ? p : null;

        // ---- Controllers (swapping a human or AI into a player) --------------

        /// <summary>
        /// Puts a controller in charge of a player. The previous controller is
        /// detached and can no longer issue commands. Passing null leaves the
        /// player idle. A controller can drive only one player at a time.
        /// </summary>
        public void SetController(Player player, IPlayerController controller)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            IPlayerController old = player.Controller;
            if (controller != null && ReferenceEquals(controller, old)) return;
            if (controller != null && controller.Player != null && controller.Player != player)
                throw new InvalidOperationException(controller.Name + " is already driving " + controller.Player.Name);

            DetachController(player);
            AttachController(player, controller ?? new IdleController());
            ControllerChanged?.Invoke(player, old, player.Controller);
        }

        /// <summary>Exchanges the controllers of two players (e.g. a human trades sides with an AI).</summary>
        public void SwapControllers(Player a, Player b)
        {
            if (a == null || b == null || a == b) return;
            IPlayerController ca = a.Controller, cb = b.Controller;
            DetachController(a);
            DetachController(b);
            AttachController(a, cb);
            AttachController(b, ca);
            ControllerChanged?.Invoke(a, ca, cb);
            ControllerChanged?.Invoke(b, cb, ca);
        }

        private void AttachController(Player player, IPlayerController controller)
        {
            if (controller.Player != null && controller.Player != player)
                throw new InvalidOperationException(controller.Name + " is already driving " + controller.Player.Name);

            var link = new ControllerLink(Commands, player, controller);
            player.Controller = controller;
            player.Link = link;
            controller.Attach(player, link);
        }

        private void DetachController(Player player)
        {
            if (player.Link != null) player.Link.Active = false;
            player.Link = null;
            player.Controller?.Detach();
            player.Controller = null;
        }

        // ---- Viewing ---------------------------------------------------------

        /// <summary>Changes whose knowledge the UI renders (hot-seat, tag switching).</summary>
        public void SetViewer(Player player)
        {
            if (player == null || player == Viewer) return;
            if (!_byId.ContainsKey(player.Id.Value)) throw new ArgumentException("Player is not in this session");
            Player previous = Viewer;
            Viewer = player;
            ViewerChanged?.Invoke(previous, player);
        }

        // ---- Notifications ---------------------------------------------------

        /// <summary>Adds to the player's inbox and tells whoever is controlling them.</summary>
        public GameNotification Notify(Player player, string kind, string title, string body,
                                       EntityId subject = default, bool requiresResponse = false)
        {
            var n = new GameNotification
            {
                Id = _nextNotificationId++, Tick = Tick, Kind = kind, Title = title, Body = body,
                Subject = subject, RequiresResponse = requiresResponse,
            };
            player.AddNotification(n);
            player.Controller?.OnNotification(n);
            NotificationPosted?.Invoke(player, n);
            return n;
        }

        // ---- Time ------------------------------------------------------------

        /// <summary>Runs one simulation tick.</summary>
        public void Advance()
        {
            Tick++;
            int count = _players.Count;
            for (int i = 0; i < count; i++)
            {
                Player p = _players[i];
                p.Controller?.Think(new ControllerContext(this, p, Tick));
            }
            Commands.Process();
        }

        /// <summary>Runs queued commands without advancing time. Safe to call every frame.</summary>
        public void ProcessCommands() => Commands.Process();
    }
}
