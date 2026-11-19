# Player / controller framework

Layout (drop `sim/` into Assets/Scripts/):

    RogueAI.Sim/   pure C# assembly (asmdef, no UnityEngine). Core, Control, Commands.
    Host/          Unity side. GameSessionBehaviour lives OUTSIDE the asmdef folder.

Idea
- Player = the actor. Holds Resources, Stats, Knowledge, Owned/Controlled, Inbox. Decides nothing.
- IPlayerController = whatever decides for a player (HumanController, AI, scripted, idle).
  It only talks to the game by submitting ICommand objects through the sink it is given.
- GameSession = players + OwnershipRegistry + CommandBus + tick + Viewer.
- Tag switching = GameSession.SetViewer (whose knowledge the UI shows),
  SetController / SwapControllers (who drives whom). State stays on the Player.

Add a command from any module:
    public sealed class MyCommand : ICommand { public int Foo; }
    session.Commands.Register<MyCommand>((session, player, cmd) => { ...; return CommandResult.Success; });
UI/AI then call  controller.Submit(new MyCommand{...})  /  SubmitCommand(...).
