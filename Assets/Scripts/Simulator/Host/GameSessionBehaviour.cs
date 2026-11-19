using System;
using System.Collections.Generic;
using UnityEngine;
using RogueAI.Sim;

/// <summary>
/// Unity host for the simulation. Creates the GameSession, adds the
/// configured players, and drives it: commands every frame, ticks at the
/// chosen speed. Other scripts reach the session via GameSessionBehaviour.Instance.
///
/// Debug hotkeys (Ctrl held): 1-4 view player 1-4; 0 toggles the viewed
/// player between a human and an idle controller; Space cycles speed.
/// </summary>
[DefaultExecutionOrder(-100)]
public class GameSessionBehaviour : MonoBehaviour
{
    public enum ControllerKind { Human, Idle }

    [Serializable]
    public class PlayerSetup
    {
        public string name = "Player";
        public Faction faction = Faction.Red;
        public ControllerKind controller = ControllerKind.Idle;
        public bool startingViewer;
    }

    [SerializeField] private List<PlayerSetup> players = new List<PlayerSetup>
    {
        new PlayerSetup { name = "Red",   faction = Faction.Red,   controller = ControllerKind.Human, startingViewer = true },
        new PlayerSetup { name = "Blue",  faction = Faction.Blue,  controller = ControllerKind.Idle },
        new PlayerSetup { name = "White", faction = Faction.White, controller = ControllerKind.Idle },
    };

    [Header("Clock")]
    [Tooltip("Real seconds per simulation tick at speed 1.")]
    [SerializeField] private float secondsPerTick = 1f;
    [Tooltip("Speed steps: multiplier per step. Step 0 should be 0 (paused).")]
    [SerializeField] private float[] speedSteps = { 0f, 1f, 2f, 5f };
    [SerializeField] private int speedIndex = 1;
    [SerializeField] private int maxTicksPerFrame = 8;

    [Header("Debug")]
    [SerializeField] private bool debugHotkeys = true;

    public static GameSessionBehaviour Instance { get; private set; }
    public GameSession Session { get; private set; }

    public int SpeedIndex
    {
        get => speedIndex;
        set => speedIndex = Mathf.Clamp(value, 0, speedSteps.Length - 1);
    }
    public bool Paused => speedSteps.Length == 0 || speedSteps[speedIndex] <= 0f;

    private float _accumulator;

    private void Awake()
    {
        Instance = this;
        Session = new GameSession();

        Player viewer = null;
        foreach (var setup in players)
        {
            IPlayerController controller = setup.controller == ControllerKind.Human
                ? (IPlayerController)new HumanController()
                : new IdleController();
            Player p = Session.AddPlayer(setup.name, setup.faction, controller);
            if (setup.startingViewer) viewer = p;
        }
        if (viewer != null) Session.SetViewer(viewer);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (debugHotkeys) HandleHotkeys();

        Session.ProcessCommands();      // human orders work while paused

        if (Paused) return;
        _accumulator += Time.deltaTime * speedSteps[speedIndex] / Mathf.Max(secondsPerTick, 0.0001f);
        int ticks = 0;
        while (_accumulator >= 1f && ticks < maxTicksPerFrame)
        {
            Session.Advance();
            _accumulator -= 1f;
            ticks++;
        }
        if (_accumulator > maxTicksPerFrame) _accumulator = 0f;      // don't spiral after a hitch
    }

    private void HandleHotkeys()
    {
        if (!(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) return;

        for (int i = 0; i < 4 && i < Session.Players.Count; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Session.SetViewer(Session.Players[i]);

        if (Input.GetKeyDown(KeyCode.Alpha0) && Session.Viewer != null)
        {
            Player v = Session.Viewer;
            Session.SetController(v, v.Controller.IsHuman ? (IPlayerController)new IdleController() : new HumanController());
        }

        if (Input.GetKeyDown(KeyCode.Space)) SpeedIndex = (speedIndex + 1) % speedSteps.Length;
    }

    private void OnGUI()
    {
        if (!debugHotkeys || Session?.Viewer == null) return;
        Player v = Session.Viewer;
        GUI.Label(new Rect(10, Screen.height - 26, 600, 22),
            "Viewing: " + v.Name + " [" + v.Controller.Name + "]   Tick " + Session.Tick +
            "   Speed " + (Paused ? "paused" : speedSteps[speedIndex] + "x"));
    }
}
