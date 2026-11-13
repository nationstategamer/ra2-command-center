#!/usr/bin/env python3
"""Write package/EnhancedRA2/ai-additions.json: only the text Smart RA2 inserts into ai.ini.

The public package never contains the game's own ai.ini. Command Center rebuilds the full
file on the player's PC from their ra2.mix plus these insertions, then checks the result
against outputSha256. Inputs: src/ai.ini and build/changes.json (both made by build_ai.py).
"""
import hashlib, json, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BASE_SHA256 = "1feac6ddea6886b177ddf7e5f8580b7a99a63f12684f2cbb42831671bb7a8a79"


def main():
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "package" / "ai-additions.json"
    generated = (ROOT / "src" / "ai.ini").read_bytes()
    changes = json.loads((ROOT / "build" / "changes.json").read_text(encoding="utf-8"))
    if hashlib.sha256(generated).hexdigest() != changes["output"]["sha256"]:
        raise SystemExit("src/ai.ini does not match build/changes.json; run build_ai.py first.")
    insertions, recovered = [], bytearray(generated)
    for item in changes["insertions"]:
        text = generated[item["output_offset"]:item["output_offset"] + item["length"]]
        if hashlib.sha256(text).hexdigest() != item["sha256"]:
            raise SystemExit("Insertion hash mismatch for " + item["registry"])
        insertions.append({"registry": item["registry"], "offset": item["original_offset"], "text": text.decode("cp1252")})
    for item in reversed(changes["insertions"]):
        del recovered[item["output_offset"]:item["output_offset"] + item["length"]]
    if hashlib.sha256(bytes(recovered)).hexdigest() != BASE_SHA256:
        raise SystemExit("Removing the insertions does not give back the stock ai.ini.")
    data = {
        "description": "Smart RA2 0.1.0 additions to Red Alert 2's ai.ini (8 task forces, 4 scripts, 8 teams, 16 triggers). "
                       "Command Center inserts each text at its offset in the game's own ai.ini from ra2.mix.",
        "baseSha256": BASE_SHA256,
        "outputSha256": changes["output"]["sha256"],
        "insertions": insertions,
    }
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(data, indent=2, ensure_ascii=True) + "\n", encoding="utf-8")
    print(f"{out}: {len(insertions)} insertions, {sum(len(i['text']) for i in insertions)} characters")


if __name__ == "__main__":
    main()
