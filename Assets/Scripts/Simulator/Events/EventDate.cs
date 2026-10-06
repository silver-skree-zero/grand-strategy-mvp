using System;

[Serializable]
public class EventDate
{
    public int year;
    public int month;
    public int day;

    public DateTime ToDateTime()
    {
        return new DateTime(
            year,
            month,
            day,
            0,
            0,
            0,
            DateTimeKind.Utc
        );
    }
}