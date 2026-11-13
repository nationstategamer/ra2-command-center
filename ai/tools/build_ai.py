#!/usr/bin/env python3
"""Build the additions-only Smart RA2 AI file from the verified stock extract.

This tool never installs files, edits rules/art, or launches the game.  Its two
inputs are immutable baseline extracts.  Output paths remain inside SmartAI.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import sys


ROOT = Path(__file__).resolve().parents[1]
AI_NAME = "ra2.mix__local.mix__ai.ini"
RULES_NAME = "ra2.mix__local.mix__rules.ini"
AI_SHA256 = "1feac6ddea6886b177ddf7e5f8580b7a99a63f12684f2cbb42831671bb7a8a79"
RULES_SHA256 = "fd1e95cea0306ea78049dc81c8cd816e18c28c496872a1ff02edd50bd082062f"
RELEASE = "0.1.0"
ENCODING = "cp1252"
NL = "\r\n"
SECTION = re.compile(rb"^\[([^\]\r\n]+)\][^\r\n]*\r?\n", re.MULTILINE)


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def parse_ini(data: bytes) -> dict[str, dict[str, str]]:
    """Read Westwood INI syntax without rewriting its comments or loose lines."""
    result: dict[str, dict[str, str]] = {}
    section = None
    for raw in data.decode(ENCODING).splitlines():
        line = raw.split(";", 1)[0].strip()
        if line.startswith("[") and "]" in line:
            section = line[1:line.index("]")]
            result.setdefault(section, {})
        elif section is not None and "=" in line:
            key, value = line.split("=", 1)
            result[section][key.strip()] = value.strip()
    return result


def verified_input(name: str, expected: str) -> bytes:
    data = (ROOT / "baseline" / name).read_bytes()
    if sha256(data) != expected:
        raise ValueError(f"Baseline hash mismatch: {name}; refusing to build.")
    return data


def comparator_at_least(count: int) -> str:
    # Native 32-byte comparator: count, operator (3 = >=), then 24 zero bytes.
    return (struct.pack("<II", count, 3) + bytes(24)).hex().upper()


def scripts() -> list[dict]:
    return [
        {
            "id": "SAI_SCRIPT_001", "name": "Smart Mixed Building Assault",
            "actions": [[54, 0], [0, 2], [49, 0], [0, 1]],
            "purpose": "Rally at the friendly base, attack structures, then other targets.",
            "native_evidence": ["0CE4EC8C-G", "0CE4BDBC-G"],
        },
        {
            "id": "SAI_SCRIPT_002", "name": "Smart Armor Response",
            "actions": [[54, 0], [0, 5], [49, 0], [0, 1]],
            "purpose": "Rally at the friendly base and prioritize ground vehicles.",
            "native_evidence": ["0A6E716C-G"],
        },
        {
            "id": "SAI_SCRIPT_003", "name": "Smart Allied Air Reserve",
            "actions": [[58, 131079], [11, 11]],
            "purpose": "Move to the friendly Allied war factory and area guard nearby.",
            "native_evidence": ["0CE4A48C-G"],
        },
        {
            "id": "SAI_SCRIPT_004", "name": "Smart Soviet Air Reserve",
            "actions": [[58, 131086], [11, 11]],
            "purpose": "Move to the friendly Soviet war factory and area guard nearby.",
            "native_evidence": ["0A4529FC-G", "0CE4A48C-G", "0D2C345C-G"],
        },
    ]


def teams() -> list[dict]:
    specs = [
        (1, "Allied Mixed Assault", 1, [(3, "MTNK"), (2, "FV")], 1, 3, "mixed_assault"),
        (2, "Soviet Mixed Assault", 2, [(3, "HTNK"), (2, "HTK")], 1, 3, "mixed_assault"),
        (3, "Allied Air Reserve", 1, [(3, "FV")], 3, 3, "air_reserve"),
        (4, "Soviet Air Reserve", 2, [(3, "HTK")], 4, 3, "air_reserve"),
        (5, "Allied Armor Response", 1, [(5, "MTNK"), (1, "FV")], 2, 3, "armor_response"),
        (6, "Soviet Armor Response", 2, [(2, "HTNK"), (3, "DRON")], 2, 4, "armor_response"),
        (7, "Allied Escorted Siege", 1, [(3, "MTNK"), (1, "FV"), (2, "SREF")], 1, 8, "siege"),
        (8, "Soviet Escorted Siege", 2, [(3, "HTNK"), (2, "HTK"), (2, "V3")], 1, 3, "siege"),
    ]
    return [
        {
            "id": f"SAI_TEAM_{number:03}", "task_force": f"SAI_TF_{number:03}",
            "name": "Smart " + name, "side": side, "role": role,
            "units": [{"count": count, "type": unit} for count, unit in units],
            "script": f"SAI_SCRIPT_{script:03}", "tech_level": tech,
            "template": "0A86A48C-G" if side == 1 else "0C31CA7C-G",
            "max": 1,
        }
        for number, name, side, units, script, tech, role in specs
    ]


def triggers(team_specs: list[dict]) -> list[dict]:
    result: list[dict] = []
    for team in team_specs:
        role, side = team["role"], team["side"]
        if role == "mixed_assault":
            conditions = [(1, "GAWEAP" if side == 1 else "NAWEAP", 1)]
        elif role == "air_reserve":
            conditions = [(0, "ORCA", 2), (0, "BEAG", 2), (0, "JUMPJET", 3), (0, "ZEP", 1)]
        elif role == "armor_response":
            conditions = [(0, "MTNK", 5), (0, "HTNK", 5)]
        else:
            conditions = [(1, "GATECH" if side == 1 else "NARADR", 1)]
        for condition_type, condition_object, count in conditions:
            start, minimum, maximum = (70, 30, 90) if role in ("air_reserve", "armor_response") else (50, 20, 70)
            result.append({
                "id": f"SAI_TRIG_{len(result) + 1:03}",
                "name": f"{team['name']} {condition_object}",
                "team": team["id"], "side": side, "owner": "<all>",
                "tech_level": team["tech_level"], "condition_type": condition_type,
                "condition_scope": "enemy" if condition_type == 0 else "own",
                "condition_object": condition_object, "condition_operator": ">=",
                "condition_count": count, "comparator_hex": comparator_at_least(count),
                "weight_start": start, "weight_min": minimum, "weight_max": maximum,
                "skirmish": True, "base_defense": False, "second_team": "<none>",
                "easy": False, "normal": True, "hard": True,
            })
    return result


def trigger_value(trigger: dict) -> str:
    return ",".join([
        trigger["name"], trigger["team"], trigger["owner"], str(trigger["tech_level"]),
        str(trigger["condition_type"]), trigger["condition_object"], trigger["comparator_hex"],
        f"{trigger['weight_start']:.6f}", f"{trigger['weight_min']:.6f}", f"{trigger['weight_max']:.6f}",
        "1", "0", str(trigger["side"]), "0", "<none>", "0", "1", "1",
    ])


def section_bytes(name: str, fields: dict[str, str]) -> bytes:
    return ("[" + name + "]" + NL + "".join(f"{key}={value}{NL}" for key, value in fields.items()) + NL).encode(ENCODING)


def build() -> dict[Path, bytes]:
    original = verified_input(AI_NAME, AI_SHA256)
    rules_bytes = verified_input(RULES_NAME, RULES_SHA256)
    stock, rules = parse_ini(original), parse_ini(rules_bytes)
    team_specs, script_specs = teams(), scripts()
    trigger_specs = triggers(team_specs)
    added_sections: dict[str, dict[str, str]] = {}

    for team in team_specs:
        task_fields = {"Name": team["name"]}
        task_fields.update({str(index): f"{unit['count']},{unit['type']}" for index, unit in enumerate(team["units"])})
        task_fields["Group"] = "-1"
        added_sections[team["task_force"]] = task_fields
        team_fields = dict(stock[team["template"]])
        team_fields.update({
            "Name": team["name"], "Script": team["script"], "TaskForce": team["task_force"],
            "Autocreate": "no", "Max": "1", "Reinforce": "no", "Prebuild": "no",
            "Recruiter": "no", "LooseRecruit": "no", "Droppod": "no",
            "AreTeamMembersRecruitable": "no", "IsBaseDefense": "no",
            "Aggressive": "no" if team["role"] == "air_reserve" else "yes",
        })
        added_sections[team["id"]] = team_fields
        team["settings"] = team_fields
        total_cost = 0
        for unit in team["units"]:
            unit_rules = rules[unit["type"]]
            unit["name"] = unit_rules["Name"]
            unit["unit_cost"] = int(unit_rules["Cost"])
            unit["stock_prerequisite"] = unit_rules["Prerequisite"]
            unit["stock_tech_level"] = int(unit_rules["TechLevel"])
            unit["stock_owner"] = unit_rules["Owner"].split(",")
            total_cost += unit["count"] * unit["unit_cost"]
        team["stock_total_cost"] = total_cost

    for script in script_specs:
        fields = {"Name": script["name"]}
        fields.update({str(index): f"{action},{argument}" for index, (action, argument) in enumerate(script["actions"])})
        added_sections[script["id"]] = fields

    registry_additions = {}
    for registry, identifiers in [
        ("TaskForces", [team["task_force"] for team in team_specs]),
        ("ScriptTypes", [script["id"] for script in script_specs]),
        ("TeamTypes", [team["id"] for team in team_specs]),
    ]:
        start = max(int(key) for key in stock[registry]) + 1
        registry_additions[registry] = {str(start + index): identifier for index, identifier in enumerate(identifiers)}
    registry_additions["AITriggerTypes"] = {trigger["id"]: trigger_value(trigger) for trigger in trigger_specs}

    all_ids = list(added_sections) + list(registry_additions["AITriggerTypes"])
    if len(set(all_ids)) != len(all_ids) or any(identifier in stock for identifier in all_ids):
        raise ValueError("Generated ID collision.")
    if any(len(identifier.encode("ascii")) > 23 for identifier in all_ids):
        raise ValueError("Generated ID exceeds native 23-byte limit.")
    if any(identifier in stock["AITriggerTypes"] for identifier in all_ids):
        raise ValueError("Generated trigger ID collision.")

    matches = list(SECTION.finditer(original))
    boundaries = {match.group(1).decode(ENCODING): (matches[index + 1].start() if index + 1 < len(matches) else len(original)) for index, match in enumerate(matches)}
    insertions = []
    for registry, fields in registry_additions.items():
        content = (f"; Smart RA2 {RELEASE}: additive {registry} entries.{NL}" + "".join(f"{key}={value}{NL}" for key, value in fields.items()) + NL).encode(ENCODING)
        if registry == "AITriggerTypes":
            # Keep the stock Digest section last and untouched. It is an opaque
            # legacy field, not a signature generated by this custom build.
            content += (f"; Smart RA2 native-engine definitions. Disable by removing this loose ai.ini.{NL}{NL}").encode(ENCODING)
            content += b"".join(section_bytes(name, values) for name, values in added_sections.items())
        insertions.append((boundaries[registry], registry, content))

    output, cursor, insertion_records = bytearray(), 0, []
    for original_offset, registry, content in sorted(insertions):
        output.extend(original[cursor:original_offset])
        insertion_records.append({
            "registry": registry, "original_offset": original_offset,
            "output_offset": len(output), "length": len(content), "sha256": sha256(content),
        })
        output.extend(content)
        cursor = original_offset
    output.extend(original[cursor:])
    generated = bytes(output)

    # Prove insertion-only preservation at byte and semantic levels.
    recovered = bytearray(generated)
    for item in reversed(insertion_records):
        del recovered[item["output_offset"]:item["output_offset"] + item["length"]]
    if bytes(recovered) != original:
        raise AssertionError("Insertion preservation check failed.")
    parsed = parse_ini(generated)
    for section, fields in stock.items():
        for key, value in fields.items():
            if parsed.get(section, {}).get(key) != value:
                raise AssertionError(f"Stock value changed: [{section}] {key}")

    design = {
        "schema_version": 1, "release": RELEASE, "title": "Smart RA2: conservative native skirmish AI",
        "scope": "Additive Normal/Hard AI teams for original Red Alert 2. Easy and all stock definitions are retained.",
        "baseline": {"ai_file": AI_NAME, "ai_sha256": AI_SHA256, "rules_file": RULES_NAME, "rules_sha256": RULES_SHA256},
        "constraints": [
            "Only the loose ai.ini is changed by this AI release; no rules/art/unit statistics/difficulty bonuses are altered.",
            "All original sections, keys, values, IDs, comments and bytes are preserved by insertions.",
            "New teams use common faction units with normal paid production; no reinforcements, prebuild, droppods or veteran grants.",
            "New triggers run only on Normal/Hard, use no secondary team, and share a Max=1 cap per team type.",
            "Autocreate=no prevents unrelated generic team creation from bypassing the conditional trigger gates.",
            "Reactive teams require only their faction war factory; siege triggers explicitly require the relevant owned technology building.",
            "No country-exclusive, crate-only, naval or aircraft units are produced by new teams.",
            "Attack scripts use friendly rally action54 and omit enemy gather action53; defensive reserves area-guard the friendly war factory.",
        ],
        "limitations": [
            "This is authored conditional AI, not a learning planner or a change to stock AI visibility/economy rules.",
            "Native triggers offer one condition; enemy-based responses cannot additionally test factory ownership in the same trigger. Their units all require a war factory, so the stock production system still needs one to build them.",
            "Mixed teams share an order but do not gain new formation, coordinated fire or escort pathfinding logic.",
            "The engine's existing target choice, gathering, movement and unreachable-path behavior remain; land-map live testing is required.",
            "Aircraft-count conditions are global enemy composition signals, not a new proximity sensor or forecast.",
            "An existing air reserve stays while its units survive; changing enemy composition does not cancel teams already built.",
            "Small weights limit disruption to stock strategies; relative frequency and effective difficulty require observed matches.",
            "Use a new skirmish for validation. Stock/campaign behavior remains subject to the engine's own ai.ini loading rules.",
        ],
        "teams": team_specs, "scripts": script_specs, "triggers": trigger_specs,
        "baseline_observations": [
            {"id": "0A44F17C-G", "finding": "Stock trigger named Soviet Dog Refinery has side=1 (Allied). Preserved, not repaired in this release."},
            {"id": "0A86ACAC-G", "finding": "Stock task force named 6 Grizzly, 1 Mirage, 1 Prism actually requests 4 MTNK, 1 MGTK, 1 SREF. Preserved."},
            {"id": "0C0D251C-G", "finding": "Stock Soviet Bombard Easy trigger has all difficulty flags disabled. Preserved."},
            {"id": "Digest", "finding": "Original opaque Digest field retained verbatim; build provenance is the SHA-256 manifest, not this legacy value."},
        ],
        "live_acceptance": [
            "Observe both Allied and Soviet Normal/Hard computers actually forming SAI_TEAM teams with expected members.",
            "Check no SAI_TEAM is created by an Easy computer.",
            "Observe anti-air reserve formation after relevant enemy air thresholds and armor-response formation after tank thresholds.",
            "Observe no new SREF team before an owned Allied battle lab and no new V3 team before an owned Soviet radar.",
            "Watch completed attack teams leave the friendly base and attack; reserves should remain guarding their factory.",
            "Save and load a new Smart RA2 skirmish, confirm continued AI production and game stability.",
        ],
    }
    manifest = {
        "schema_version": 1, "release": RELEASE,
        "inputs": [
            {"path": "baseline/" + AI_NAME, "size": len(original), "sha256": AI_SHA256},
            {"path": "baseline/" + RULES_NAME, "size": len(rules_bytes), "sha256": RULES_SHA256, "usage": "read-only metadata"},
        ],
        "output": {"path": "src/ai.ini", "size": len(generated), "sha256": sha256(generated)},
        "stock_preservation": {"byte_exact_after_removing_recorded_insertions": True, "sections": len(stock), "entries": sum(len(fields) for fields in stock.values()), "modified_stock_values": 0, "removed_stock_values": 0},
        "counts": {"new_task_forces": len(team_specs), "new_scripts": len(script_specs), "new_teams": len(team_specs), "new_triggers": len(trigger_specs)},
        "insertions": insertion_records, "registry_additions": registry_additions,
        "added_sections": added_sections,
        "stock_counts": {key: len(stock[key]) for key in registry_additions},
        "output_counts": {key: len(parsed[key]) for key in registry_additions},
        "not_modified": ["rules.ini", "art.ini", "unit statistics", "difficulty bonuses", "stock AI entries", "runtime game directory", "saved games"],
        "installation": "Not installed by this tool. Parent review, independent validation and separate-edition deployment required.",
    }
    encode_json = lambda value: (json.dumps(value, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
    return {ROOT / "src" / "ai.ini": generated, ROOT / "design.json": encode_json(design), ROOT / "build" / "changes.json": encode_json(manifest)}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Verify checked-in outputs match a deterministic rebuild without writing anything.")
    args = parser.parse_args()
    outputs = build()
    failures = []
    for path, data in outputs.items():
        if args.check:
            if not path.is_file() or path.read_bytes() != data:
                failures.append(str(path.relative_to(ROOT)))
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
    if failures:
        print("Rebuild mismatch: " + ", ".join(failures), file=sys.stderr)
        return 1
    print(json.dumps({"mode": "checked" if args.check else "built", "release": RELEASE, "outputs": [{"path": str(path.relative_to(ROOT)), "bytes": len(data), "sha256": sha256(data)} for path, data in outputs.items()]}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
