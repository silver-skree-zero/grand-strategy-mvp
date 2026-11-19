namespace RogueAI.Sim
{
    /// <summary>
    /// Something the game tells a player: a proposal awaiting approval, a
    /// detection alert, a result. Kept on the Player (not the controller) so
    /// the inbox survives controller swaps.
    /// </summary>
    public sealed class GameNotification
    {
        public int Id { get; internal set; }
        public int Tick { get; internal set; }
        public string Kind { get; internal set; }       // e.g. "proposal", "alert", "result"
        public string Title { get; internal set; }
        public string Body { get; internal set; }
        public EntityId Subject { get; internal set; }  // what it is about, if anything
        public bool RequiresResponse { get; internal set; }
        public bool Resolved { get; internal set; }
    }
}
