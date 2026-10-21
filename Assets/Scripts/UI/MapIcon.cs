using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// One billboard icon on the globe: a city, a strategic facility, or (later)
/// a moving unit. Static icons are loaded from map_icons.json; dynamic ones
/// are created from code through MapIconLayer.AddIcon / SetPosition.
/// </summary>
public class MapIcon
{
    // ---- Loaded from JSON --------------------------------------------------
    public string id;          // stable unique key, e.g. "city_USA_new_york", "asset_tsmc_hsinchu"
    public string name;        // display name
    public string type;        // matches an IconTypeStyle name: city, capital, datacenter, fab, ...
    public double lat;         // degrees, -90..90
    public double lon;         // degrees, -180..180
    public int priority;       // 0..100; higher survives declutter and shows when zoomed out
    public string country;     // ISO A3 of the country it sits in (informational for now)
    public long pop;           // population for cities, 0 otherwise
    public string detail;      // free text for the info panel

    // ---- Runtime only ------------------------------------------------------
    [JsonIgnore] public int index;            // slot in the layer's arrays (managed by the layer)
    [JsonIgnore] public int styleIndex;       // resolved style for `type`
    [JsonIgnore] public object tag;           // game code can hang an actor / organization / unit here
}

public class MapIconFile
{
    public List<MapIcon> icons;
}
