"""Adds a release to the Jellyfin plugin repository manifest (manifest.json).

Usage: python3 tools/update_manifest.py <manifest.json> <build.yaml> <version> <zip-url> <md5> <owner/repo>

The manifest format is the one Jellyfin reads from plugin repositories: a list of plugins, each with its
versions (version, changelog, targetAbi, sourceUrl, checksum = MD5 of the zip, timestamp).
"""
import json
import re
import sys
from datetime import datetime, timezone


def read_build_yaml(path):
    """Minimal reader for the flat build.yaml of this repository (quoted values and folded blocks)."""
    values, key, block = {}, None, []
    for line in open(path, encoding="utf-8").read().splitlines():
        if key and (line.startswith("  ") or not line.strip()):
            block.append(line.strip())
            continue
        if key:
            values[key] = " ".join(x for x in block if x)
            key, block = None, []
        m = re.match(r'^(\w+):\s*(.*)$', line)
        if not m:
            continue
        name, value = m.groups()
        if value == ">":
            key = name
        elif value:
            values[name] = value.strip('"')
    if key:
        values[key] = " ".join(x for x in block if x)
    return values


def main(manifest_path, build_path, version, url, checksum, repository):
    build = read_build_yaml(build_path)
    try:
        manifest = json.load(open(manifest_path, encoding="utf-8"))
    except FileNotFoundError:
        manifest = []

    plugin = next((p for p in manifest if p.get("guid") == build["guid"]), None)
    if plugin is None:
        plugin = {"guid": build["guid"], "versions": []}
        manifest.append(plugin)

    plugin.update({
        "name": build["name"],
        "description": build.get("description", ""),
        "overview": build.get("overview", ""),
        "owner": repository.split("/")[0],
        "category": build.get("category", "General"),
        "imageUrl": f"https://raw.githubusercontent.com/{repository}/main/images/logo.png",
    })
    plugin["versions"] = [v for v in plugin.get("versions", []) if v.get("version") != version]
    plugin["versions"].insert(0, {
        "version": version,
        "changelog": build.get("changelog", ""),
        "targetAbi": build["targetAbi"],
        "sourceUrl": url,
        "checksum": checksum,
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    })

    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
        f.write("\n")


if __name__ == "__main__":
    if len(sys.argv) != 7:
        sys.exit(__doc__)
    main(*sys.argv[1:])
