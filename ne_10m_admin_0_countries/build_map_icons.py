#!/usr/bin/env python3
"""
Build map_icons.json for the Unity icon layer.

Cities come from Natural Earth populated places (ne_10m_populated_places.shp,
the same download you already use for borders). Strategic assets come from a
hand-maintained JSON file (see strategic_assets_seed.json).

Usage:
  python build_map_icons.py --places path/to/ne_10m_populated_places.shp \
      --assets strategic_assets_seed.json --out map_icons.json --min-pop 300000

Priority (0-100, higher survives zoom-out and declutter):
  cities   : log scale of population (100k -> ~15, 1M -> ~40, 10M -> ~70),
             +5 for megacities, capped at 95.
  capitals : national capitals are ALWAYS included regardless of --min-pop,
             typed "capital" (star icon), priority = pop-based + 15 but at least 50.
  assets   : taken from the seed file (default 80).
"""
import argparse, json, math, re, sys


CAPITAL_MIN_PRIORITY = 50


def slug(s):
    return re.sub(r"[^a-z0-9]+", "_", s.lower()).strip("_")


def city_priority(pop, is_capital, is_mega):
    p = 15 + 25 * (math.log10(max(pop, 1)) - 5)       # 1e5 -> 15, 1e6 -> 40, 1e7 -> 65
    if is_capital: p = max(p + 15, CAPITAL_MIN_PRIORITY)   # small capitals still show at mid zoom
    if is_mega: p += 5
    return int(max(0, min(95, round(p))))


def load_cities(path, min_pop):
    import geopandas as gpd                            # imported lazily so --assets-only works
    gdf = gpd.read_file(path)
    cols = {c.upper(): c for c in gdf.columns}
    def col(row, name, default=None):
        c = cols.get(name)
        v = row[c] if c else default
        return default if v is None or (isinstance(v, float) and math.isnan(v)) else v

    icons, seen = [], set()
    for _, row in gdf.iterrows():
        pop = int(col(row, "POP_MAX", 0) or 0)
        cls = str(col(row, "FEATURECLA", "") or "").lower()
        # National capitals only: ADM0CAP flag, or the "Admin-0 capital" classes.
        # ("Admin-0 region capital" and "Admin-1 capital" are provincial, not national.)
        if "ADM0CAP" in cols:
            is_cap = int(col(row, "ADM0CAP", 0) or 0) == 1
        else:
            is_cap = cls in ("admin-0 capital", "admin-0 capital alt")
        if pop < min_pop and not is_cap:       # capitals are always kept
            continue
        name = str(col(row, "NAME", "") or col(row, "NAMEASCII", ""))
        iso = str(col(row, "ADM0_A3", "") or "")
        is_mega = int(col(row, "MEGACITY", 0) or 0) == 1
        pt = row.geometry
        base = f"city_{iso}_{slug(name)}"
        cid, n = base, 2
        while cid in seen:
            cid = f"{base}_{n}"; n += 1
        seen.add(cid)
        icons.append({
            "id": cid, "name": name,
            "type": "capital" if is_cap else "city",
            "lat": round(float(pt.y), 4), "lon": round(float(pt.x), 4),
            "priority": city_priority(pop, is_cap, is_mega),
            "country": iso, "pop": pop, "detail": "",
        })
    return icons


def load_assets(path):
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    out = []
    for a in data.get("assets", []):
        out.append({
            "id": a["id"], "name": a["name"], "type": a["type"],
            "lat": float(a["lat"]), "lon": float(a["lon"]),
            "priority": int(a.get("priority", 80)),
            "country": a.get("country", ""), "pop": 0,
            "detail": a.get("detail", ""),
        })
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--places", help="ne_10m_populated_places shapefile (omit for assets only)")
    ap.add_argument("--assets", help="strategic assets JSON")
    ap.add_argument("--out", default="map_icons.json")
    ap.add_argument("--min-pop", type=int, default=1000000)
    a = ap.parse_args()

    icons = []
    if a.places: icons += load_cities(a.places, a.min_pop)
    if a.assets: icons += load_assets(a.assets)
    if not icons:
        sys.exit("Nothing to write: pass --places and/or --assets")

    ids = [i["id"] for i in icons]
    dupes = {i for i in ids if ids.count(i) > 1}
    if dupes: sys.exit(f"Duplicate ids: {sorted(dupes)}")

    with open(a.out, "w", encoding="utf-8") as f:
        json.dump({"icons": icons}, f, ensure_ascii=False, indent=1)
    cities = sum(1 for i in icons if i["type"] in ("city", "capital"))
    print(f"Wrote {len(icons)} icons ({cities} cities, {len(icons)-cities} assets) -> {a.out}")


if __name__ == "__main__":
    main()
