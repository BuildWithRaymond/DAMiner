"""Normalize route metadata; read references without modifying either source app."""

import argparse
import base64
import collections
import copy
import hashlib
import json
import re
from pathlib import Path


def inside(record, tile):
    return (
        isinstance(tile, dict)
        and type(tile.get("X")) is int
        and type(tile.get("Y")) is int
        and 0 <= tile["X"] < record["width"]
        and 0 <= tile["Y"] < record["height"]
    )


def normalize_records(entries):
    records = {}
    for entry in copy.deepcopy(entries):
        if (
            type(entry.get("id")) is not int
            or entry["id"] < 0
            or entry["id"] in records
            or any(type(entry.get(key)) is not int or entry[key] <= 0 for key in ("width", "height"))
            or not isinstance(entry.get("portals"), list)
        ):
            raise ValueError("Invalid map dimensions, portal list, or duplicate ID")
        records[entry["id"]] = entry
    excluded = []
    for record in records.values():
        valid = []
        for portal in record["portals"]:
            destination = records.get(portal.get("map"))
            if not inside(record, portal.get("from")) or destination is None or not inside(destination, portal.get("to")):
                excluded.append({"sourceMap": record["id"], "edge": portal, "reason": "Missing endpoint or coordinate outside map bounds"})
            elif portal not in valid:
                valid.append(portal)
        record["portals"] = valid
        if "internalTransfers" in record:
            transfers = []
            for transfer in record["internalTransfers"]:
                if inside(record, transfer.get("from")) and inside(record, transfer.get("to")):
                    if transfer not in transfers:
                        transfers.append(transfer)
                else:
                    excluded.append({"sourceMap": record["id"], "edge": transfer, "reason": "Internal transfer outside map bounds"})
            record["internalTransfers"] = transfers
    return records, excluded


def legacy_hints(source, records):
    assignments = {name: int(number) for name, number in re.findall(r"(mappedMaps\d*)\.Number\s*=\s*(\d+)", source)}
    tuples = set()
    for variable, destination, x, y in re.findall(
        r"(mappedMaps\d*)\.ConnectedTo\.Add\((\d+), new Location\((\d+),\s*(\d+)\)\)", source
    ):
        if variable in assignments:
            tuples.add((assignments[variable], int(destination), int(x), int(y)))
    if not assignments or not tuples:
        raise ValueError("Excalibur legacy map declarations were not recognized")
    references = set(assignments.values()) | {t[1] for t in tuples}
    grouped = collections.defaultdict(set)
    for source_map, destination, x, y in tuples:
        grouped[(source_map, x, y)].add(destination)
    digest = hashlib.sha256(source.encode("utf-8")).hexdigest()
    hints, excluded = [], []
    for source_map, destination, x, y in sorted(tuples):
        record = records.get(source_map)
        point = {"X": x, "Y": y}
        same = [] if record is None else [p for p in record["portals"] if p["from"] == point]
        if any(p["map"] == destination for p in same):
            continue
        world = any(p.get("worldMap", False) for p in same)
        reason = ""
        if source_map == destination:
            reason = "Same-map transfer requires a known landing"
        elif record is None or destination not in records:
            reason = "Missing endpoint dimensions"
        elif not inside(record, point):
            reason = "Source trigger outside recorded dimensions"
        elif any(transfer.get("Map", transfer.get("map")) == destination for transfer in record.get("npcTransfers", [])):
            reason = "NPC service approach requires guarded dialogue, not a walking exit"
        elif any(t["from"] == point for t in record.get("internalTransfers", [])):
            reason = "Conflicts with an internal transfer"
        elif any(not p.get("worldMap", False) and p["map"] != destination for p in same):
            reason = "Conflicts with a current ordinary portal"
        elif not world and len(grouped[(source_map, x, y)]) > 1:
            reason = "Shared trigger has no corroborated world-map behavior"
        hint = {"sourceMap": source_map, "destinationMap": destination, "from": point, "worldMap": world}
        if reason:
            excluded.append(dict(hint, reason=reason))
        else:
            hints.append(dict(hint, source="Excalibur legacy walker", sourceSha256=digest))
    return hints, excluded, references


def validate_collision(pack, records):
    digest = lambda text: isinstance(text, str) and re.fullmatch(r"[0-9A-Fa-f]{64}", text) is not None
    if pack.get("schemaVersion") != 1 or pack.get("serverProfile") != "datester-7.41" or not digest(pack.get("sotpSha256")):
        raise ValueError("Invalid collision pack schema or server profile")
    seen = set()
    for grid in pack["maps"]:
        record = records.get(grid["id"])
        if record is None or grid["id"] in seen or any(grid[key] != record[key] for key in ("width", "height")) or not digest(grid.get("sourceSha256")):
            raise ValueError("Collision dimensions, map ID, or source digest do not match")
        cells = grid["width"] * grid["height"]
        bits = base64.b64decode(grid["blockedBits"], validate=True)
        if len(bits) != (cells + 7) // 8 or cells % 8 and bits[-1] >> (cells % 8):
            raise ValueError("Invalid collision bit length or padding")
        seen.add(grid["id"])
    result = copy.deepcopy(pack)
    result["missingMapIds"] = sorted(set(records) - seen)
    return result, seen


def bank_topology(records, hints, bank):
    reverse = collections.defaultdict(set)
    for record in records.values():
        for portal in record["portals"]:
            reverse[portal["map"]].add(record["id"])
    for hint in hints:
        reverse[hint["destinationMap"]].add(hint["sourceMap"])
    distances = {bank: 0}
    pending = collections.deque([bank])
    while pending:
        map_id = pending.popleft()
        for parent in reverse[map_id]:
            if parent not in distances:
                distances[parent] = distances[map_id] + 1
                pending.append(parent)
    return distances


def coverage(records, hints, geometry, references, excluded_edges, excluded_hints, bank=167):
    modern = bank_topology(records, [], bank)
    combined = bank_topology(records, hints, bank)
    maps = []
    for map_id in sorted(set(records) | references):
        record = records.get(map_id)
        maps.append({"id": map_id, "name": record["name"] if record else f"Map {map_id}",
                     "dimensionsKnown": record is not None, "packagedGeometry": map_id in geometry,
                     "modernBankTopology": map_id in modern, "combinedBankTopology": map_id in combined,
                     "reason": "Missing dimensions and geometry" if record is None else "No recorded bank connection" if map_id not in combined else "Missing packaged geometry" if map_id not in geometry else ""})
    return {"schemaVersion": 1, "bankMap": bank,
            "note": "Topology is recorded connectivity, not proof of current server walkability. Unknown landings require fresh server replanning.",
            "summary": {"referencedMaps": len(maps), "dimensionedMaps": len(records), "packagedGeometry": len(geometry),
                        "modernBankTopology": len(modern), "combinedBankTopology": len(combined), "acceptedExitHints": len(hints)},
            "maps": maps, "excludedEdges": excluded_edges, "excludedHints": excluded_hints}


def write_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--routes", type=Path, required=True)
    parser.add_argument("--excalibur-walker", type=Path, required=True)
    parser.add_argument("--collision", type=Path, required=True)
    parser.add_argument("--output-directory", type=Path, default=Path("content"))
    parser.add_argument("--report", type=Path, default=Path("artifacts/navigation-coverage.json"))
    args = parser.parse_args()
    records, excluded_edges = normalize_records(json.loads(args.routes.read_text(encoding="utf-8-sig")))
    hints, excluded_hints, references = legacy_hints(args.excalibur_walker.read_text(encoding="utf-8-sig"), records)
    collision, geometry = validate_collision(json.loads(args.collision.read_text(encoding="utf-8-sig")), records)
    report = coverage(records, hints, geometry, references, excluded_edges, excluded_hints)
    # Preserve the catalog's established order so metadata updates stay reviewable.
    write_json(args.output_directory / "routes.json", list(records.values()))
    write_json(args.output_directory / "route-hints.json", hints)
    write_json(args.output_directory / "maps/datester-7.41.walkability.json", collision)
    write_json(args.output_directory / "navigation-coverage.json", report)
    write_json(args.report, report)
    print(json.dumps(report["summary"]))


if __name__ == "__main__":
    main()
