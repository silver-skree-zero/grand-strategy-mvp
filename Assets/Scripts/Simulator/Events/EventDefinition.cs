using System;

[Serializable]
public class EventDefinition
{
    public string id;
    public string title;
    public string description;

    public EventScope scope;
    public string triggerType;

    public bool fireOnlyOnce;

    public int mtthDays;

    public EventDate fixedDate;

    public string image;
}