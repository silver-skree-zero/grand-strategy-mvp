using System;
using UnityEngine;

public class Simulator : MonoBehaviour
{
    [SerializeField] private int year = 2026;
    [SerializeField] private int month = 6;
    [SerializeField] private int day = 21;

    [SerializeField] private int hour = 12;
    [SerializeField] private int minute = 0;
    [SerializeField] private int second = 0;

    [SerializeField] private float timeScale = 1.0f;

    public int Year => year;
    public int Month => month;
    public int Day => day;
    public int Hour => hour;
    public int Minute => minute;
    public int Second => second;

    public float TimeScale => timeScale;

    public DateTime Utc =>
        new DateTime(
            year,
            month,
            day,
            hour,
            minute,
            second,
            DateTimeKind.Utc
        );

    private void Update()
    {
        if (timeScale == 0)
            return;

        DateTime newTime =
            Utc.AddSeconds(Time.deltaTime * timeScale);

        year = newTime.Year;
        month = newTime.Month;
        day = newTime.Day;
        hour = newTime.Hour;
        minute = newTime.Minute;
        second = newTime.Second;
    }

    public void SetTimeScale(float scale)
    {
        timeScale = scale;
    }
}