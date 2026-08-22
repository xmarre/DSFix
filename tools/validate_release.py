from __future__ import annotations

import argparse
import pathlib
import re
import sys
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
EXPECTED_VERSION = "1.7.4"


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
        "MatchesCurrentFleeTarget",
        'ReflectionUtil.ReadMember(__0, "Troops")',
        "ShowBattleResults",
        "ExpectedRewriteCount = 3",
        "GenerateHeroFirstName",
    ):
        if required not in source:
            fail(f"main source missing required compatibility hook: {required}")

    if re.search(r"\bStack\s*<", source):
        fail("Stack<T> reintroduced into DSFix.dll source; this regresses the v1.7.1 Bannerlord 1.3.15 loader failure")

    for required in (
        "private static Exception RemoveTroopFinalizer",
        "__exception is IndexOutOfRangeException",
        "MatchesCurrentFleeTarget(__instance, __0)",
        "ReferenceEquals(troop, context.Wanderer)",
        "ReferenceEquals(roster, context.Roster)",
    ):
        if required not in source:
            fail(f"lord-promotion exact-target guard missing: {required}")

    for required in (
        "MethodInfo externalNamesGetter = FindExternalNamesGetter(managerType);",
        "if (externalNamesGetter != null)",
        "return matches.Length == 1 ? matches[0] : null;",
        'private const string ExternalNamesMemberName = "using_extern_namelist";',
        "ReflectionUtil.ReadMember(__instance, ExternalNamesMemberName)",
        "ReflectionUtil.WriteMember(__instance, ExternalNamesMemberName, false)",
        "ReflectionUtil.WriteMember(context.ExternalNamesOwner, ExternalNamesMemberName, context.ExternalNamesOriginalValue)",
    ):
        if required not in source:
            fail(f"optional external-name compatibility guard missing: {required}")

    lore_source = (ROOT / "DSFix" / "LoreNamePatch.cs").read_text(encoding="utf-8")
    first_name_target = re.search(
        r"private static MethodInfo FindFirstNameTarget\(Type nameGeneratorType\)(.*?)private static void PromotionPrefix",
        lore_source,
        re.S,
    )
    if not first_name_target:
        fail("could not locate FindFirstNameTarget for Bannerlord 1.3.15 validation")
    first_name_body = first_name_target.group(1)
    if "nameGeneratorType.GetMethods(ReflectionUtil.AllInstance)" not in first_name_body:
        fail("GenerateHeroFirstName target is not bound as an instance method for Bannerlord 1.3.15")
    if "ReflectionUtil.AllStatic" in first_name_body:
        fail("GenerateHeroFirstName target regressed to static lookup; Bannerlord 1.3.15 exposes it as an instance method")

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
