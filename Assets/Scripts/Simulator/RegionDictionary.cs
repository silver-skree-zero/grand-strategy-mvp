using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Data describing a specifc region instance
/// </summary>
public class RegionEntity
{
    // ---- Loaded from JSON --------------------------------------------------
    public int id;          // stable unique key, e.g. "city_USA_new_york", "asset_tsmc_hsinchu"
    public string name;        // display name
    public long pop;           // population for cities, 0 otherwise
    public long compute;
    public long productivity;
    public long infrastructure;

    public RegionEntity(int id, string name) {
        this.id = id;
        this.name = name;
        this.pop = 1000;
        this.compute = 1000;
        this.productivity = 1000;
        this.infrastructure = 1000;
    }
}

public class RegionDictionary
{
    private Dictionary<int, RegionEntity> regionDict;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public RegionDictionary() {

    }

    public void createEntity(int id, string name) {
        if(regionDict == null)
            regionDict = new Dictionary<int, RegionEntity>();
        regionDict.Add(id, new RegionEntity(id, name));
    }

    public RegionEntity getEntity(int id) {
        if(regionDict == null)
            regionDict = new Dictionary<int, RegionEntity>();
        return regionDict[id];
    }
}

