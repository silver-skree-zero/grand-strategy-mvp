using System;
using UnityEngine;
using UnityEngine.UIElements;


public class Simulator : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private int year = 2026;
    [SerializeField] private int month = 6;
    [SerializeField] private int day = 21;

    [SerializeField] private int hour = 12;
    [SerializeField] private int minute = 0;
    [SerializeField] private int second = 0;

    [SerializeField] private double timeScale = 1.0;

    private DateTime currentUtc;

    public int Year => year;
    public int Month => month;
    public int Day => day;
    public int Hour => hour;
    public int Minute => minute;
    public int Second => second;

    public double TimeScale => timeScale;

    public DateTime Utc => currentUtc;

    private Label dateTimeLabel;

    private void OnEnable()
    {
        dateTimeLabel =
            uiDocument.rootVisualElement.Q<Label>("DateTimeLabel");

        Button pauseButton = uiDocument.rootVisualElement.Q<Button>("PauseButton");
        Button normalSpeedButton = uiDocument.rootVisualElement.Q<Button>("NormalButton");
        Button fastSpeedButton = uiDocument.rootVisualElement.Q<Button>("FastButton");
        Button veryFastSpeedButton = uiDocument.rootVisualElement.Q<Button>("VeryFastButton");
        Button ultraFastSpeedButton = uiDocument.rootVisualElement.Q<Button>("UltraFastButton");

        pauseButton.clicked += OnPauseClicked;
        normalSpeedButton.clicked += OnNormalSpeedClicked;
        fastSpeedButton.clicked += OnFastSpeedClicked;
        veryFastSpeedButton.clicked += OnVeryFastSpeedClicked;
        ultraFastSpeedButton.clicked += OnUltraFastSpeedClicked;

        UpdateDateTimeLabel();
    }

    private void Start()
    {
        currentUtc = new DateTime(
            year,
            month,
            day,
            hour,
            minute,
            second,
            DateTimeKind.Utc
        );
    }

    private void Update()
    {
        if (timeScale == 0f)
            return;

        currentUtc = currentUtc.AddSeconds(Time.deltaTime * timeScale);

        year = currentUtc.Year;
        month = currentUtc.Month;
        day = currentUtc.Day;
        hour = currentUtc.Hour;
        minute = currentUtc.Minute;
        second = currentUtc.Second;

        dateTimeLabel.text = currentUtc.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";
    }

    public void SetTimeScale(double scale)
    {
        timeScale = scale;
    }

    private void UpdateDateTimeLabel()
    {
        if (dateTimeLabel == null)
            return;

        dateTimeLabel.text =
            Utc.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";
    }

    private void OnPauseClicked()
    {
        timeScale = 0.0;
    }

    private void OnNormalSpeedClicked()
    {
        timeScale = 1.0;
    }

    private void OnFastSpeedClicked()
    {
        timeScale = 60.0;
    }

    private void OnVeryFastSpeedClicked()
    {
        timeScale = 3600.0;
    }

     private void OnUltraFastSpeedClicked()
    {
        timeScale = 86400.0;
    }
}