using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class EventService : MonoBehaviour
{
    [SerializeField]
    private string eventFileName = "events.json";

    private EventDatabase database;

    public IReadOnlyList<EventDefinition> Events =>
        database?.events;
        
    private HashSet<string> firedEvents = new();

    private void Awake()
    {
        LoadEvents();
    }
    
    public bool HasFired(string eventId)
    {
        return firedEvents.Contains(eventId);
    }

    public void MarkFired(string eventId)
    {
        firedEvents.Add(eventId);
    }
    private void LoadEvents()
    {
        string path = Path.Combine(
            Application.streamingAssetsPath,
            eventFileName
        );

        if (!File.Exists(path))
        {
            Debug.LogError($"Event file not found: {path}");
            return;
        }

        string json = File.ReadAllText(path);

        Debug.Log($"Read {json.Length} characters from event file.");

        database = JsonUtility.FromJson<EventDatabase>(json);

        if (database == null || database.events == null)
        {
            Debug.LogError("Failed to load event database.");
            return;
        }

        Debug.Log($"Loaded {database.events.Count} events.");
    }
}