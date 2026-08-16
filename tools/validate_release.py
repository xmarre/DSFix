from __future__ import annotations

import argparse
import pathlib
import re
import sys
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
EXPECTED_VERSION = "1.7.2"


def fail(message: str) -> None:
    print(f"ERROR: {message}", file=sys.stderr)
    raise SystemExit(1)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--build-root", type=pathlib.Path)
    parser.add_argument("--zip", dest="zip_path", type=pathlib.Path)
    args = parser.parse_args()

    props = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
    if f"<Version>{EXPECTED_VERSION}</Version>" not in props:
        fail("Directory.Build.props version mismatch")

    submodule = (ROOT / "DSFix" / "SubModule.xml").read_text(encoding="utf-8")
    if f'<Version value="v{EXPECTED_VERSION}" />' not in submodule:
        fail("SubModule.xml version mismatch")
    for required in ("Bannerlord.Harmony", "TOR_Core", "DistinguishedService", "DSFix.InBattleNaming.dll"):
        if required not in submodule:
            fail(f"SubModule.xml missing {required}")

    source = "\n".join(p.read_text(encoding="utf-8") for p in (ROOT / "DSFix").glob("*.cs"))
    for required in (
        "FleeToOtherClanLord",
        "RemoveTroopPrefix",
        "RemoveTroopFinalizer",
        "ShowBattleResults",
        "ExpectedRewriteCount = 3",
        "GenerateHeroFirstName",
    ):
        if required not in source:
            fail(f"main source missing required compatibility hook: {required}")

    if re.search(r"\bStack\s*<", source):
        fail("Stack<T> reintroduced into DSFix.dll source; this regresses the v1.7.1 Bannerlord 1.3.15 loader failure")

    finalizer_block = re.search(r"RemoveTroopFinalizer\(.*?\n\s*}\n", source, re.S)
    if not finalizer_block or "IndexOutOfRangeException" not in finalizer_block.group(0) or "_fleeToOtherClanLordDepth" not in finalizer_block.group(0):
        fail("lord-promotion exception fallback is not narrowly scoped")

    naming_source = "\n".join(p.read_text(encoding="utf-8") for p in (ROOT / "DSFix.InBattleNaming").glob("*.cs"))
    for required in ("AssignSkills", "AssignSkillsRandomly", "GetNameSuffix", "PromoteUnit"):
        if required not in naming_source:
            fail(f"in-battle naming source missing {required}")

    if args.build_root:
        main_dll = args.build_root / "DSFix" / "DSFix.dll"
        naming_dll = args.build_root / "DSFix.InBattleNaming" / "DSFix.InBattleNaming.dll"
        for dll in (main_dll, naming_dll):
            if not dll.is_file() or dll.stat().st_size < 4096:
                fail(f"missing or implausibly small build output: {dll}")

    if args.zip_path:
        with zipfile.ZipFile(args.zip_path) as archive:
            names = set(archive.namelist())
            required = {
                "DSFix/SubModule.xml",
                "DSFix/bin/Win64_Shipping_Client/DSFix.dll",
                "DSFix/bin/Win64_Shipping_Client/DSFix.InBattleNaming.dll",
                "DSFix/README.txt",
                "DSFix/CHANGELOG.txt",
                "DSFix/VALIDATION.txt",
            }
            missing = required - names
            if missing:
                fail(f"release ZIP missing: {sorted(missing)}")
            bad = archive.testzip()
            if bad:
                fail(f"corrupt ZIP member: {bad}")

    print("DSFix validation passed")


if __name__ == "__main__":
    main()
