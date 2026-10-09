"""
state_border_segments.py

Extracts, for every pair of touching (final, post-merge) states, the exact
shared boundary linework between them -- plus each state's "outer" boundary
(coastline, or a border with anything not in the dataset) -- so Unity can
rebuild the visible border mesh whenever ownership changes:

    pair (a, b), b != 0 : draw only if owner(a) != owner(b)
    pair (a, 0)         : outer boundary, always drawn

Because the geometries come out of the topology step, neighbors share
identical vertices, so intersecting their boundaries yields exact shared
lines (no gaps or doubled borders). States absorbed into another state are
unioned into it first, so the line between a state and its absorbed
neighbor disappears automatically.

Called from generate_state_id_map.py; not meant to be run on its own.

Requirements:
    pip install shapely
"""

from shapely import STRtree
from shapely.geometry import LineString
from shapely.ops import linemerge, unary_union


def _lines_of(geom):
    """Flattens any geometry into a list of LineStrings (points are dropped)."""
    if geom is None or geom.is_empty:
        return []
    t = geom.geom_type
    if t == "LineString":
        return [geom]
    if t == "LinearRing":
        return [LineString(geom.coords)]
    if t in ("MultiLineString", "GeometryCollection"):
        out = []
        for g in geom.geoms:
            out.extend(_lines_of(g))
        return out
    return []


def _merged_lines(lines):
    """Joins end-to-end pieces into as few polylines as possible."""
    if not lines:
        return []
    return _lines_of(linemerge(lines))


def _coords(line, precision=4):
    return [[round(x, precision), round(y, precision)] for x, y in line.coords]


def extract_border_segments(members, progress_every=1000):
    """
    members: {final_state_id: [shapely geometries]} -- every original feature
             that ended up as that state (a normal state has one; a state
             that absorbed others has several).
    Returns a list of {"a": id, "b": id_or_0, "lines": [[[lon, lat], ...], ...]}.
    """
    # 1. One geometry per final state.
    final = {}
    for sid, geoms in members.items():
        if len(geoms) == 1:
            final[sid] = geoms[0]
            continue
        try:
            final[sid] = unary_union(geoms)
        except Exception:
            final[sid] = unary_union([g.buffer(0) for g in geoms])

    ids = sorted(final)
    geoms = [final[i] for i in ids]
    boundaries = [g.boundary for g in geoms]

    # 2. Candidate neighbor pairs from bounding-box overlap.
    tree = STRtree(geoms)
    query_idx, tree_idx = tree.query(geoms, predicate="intersects")
    candidates = sorted({(min(int(a), int(b)), max(int(a), int(b)))
                         for a, b in zip(query_idx, tree_idx) if a != b})
    print(f"  {len(ids)} states, {len(candidates)} candidate neighbor pairs")

    # 3. Shared boundary for each pair.
    segments = []
    shared_per_state = {i: [] for i in range(len(ids))}
    for n, (i, j) in enumerate(candidates):
        if progress_every and n and n % progress_every == 0:
            print(f"  ...{n}/{len(candidates)} pairs")
        shared = _merged_lines(_lines_of(boundaries[i].intersection(boundaries[j])))
        if not shared:
            continue  # touch at a single point only
        shared_per_state[i].extend(shared)
        shared_per_state[j].extend(shared)
        segments.append({"a": ids[i], "b": ids[j],
                         "lines": [_coords(l) for l in shared]})

    # 4. Outer boundary: whatever isn't shared with another state.
    outer_count = 0
    for i, sid in enumerate(ids):
        shared = shared_per_state[i]
        remaining = (boundaries[i].difference(unary_union(shared))
                     if shared else boundaries[i])
        outer = _merged_lines(_lines_of(remaining))
        if outer:
            outer_count += 1
            segments.append({"a": sid, "b": 0,
                             "lines": [_coords(l) for l in outer]})

    print(f"  {len(segments) - outer_count} shared borders, "
          f"{outer_count} outer boundaries")
    return segments