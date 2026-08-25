from __future__ import annotations

import argparse
import pathlib
import re
import sys
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
EXPECTED_VERSION = "1.7.6"


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
        "PromotionIdentityPatch.TryPatch()",
        "AddBehavior(new PromotionIdentityCampaignBehavior())",
    ):
        if required not in source:
            fail(f"main source missing required compatibility hook: {required}")

    if re.search(r"\bStack\s*<", source):
        fail("Stack<T> reintroduced into DSFix.dll source; this regresses the v1.7.1 Bannerlord 1.3.15 loader failure")

    roster_source = (ROOT / "DSFix" / "LordPromotionRosterPatch.cs").read_text(encoding="utf-8")
    for required in (
        "private static Exception RemoveTroopFinalizer",
        "__exception is IndexOutOfRangeException",
        "MatchesCurrentFleeTarget(__instance, __0)",
        "ReferenceEquals(troop, context.Wanderer)",
        "ReferenceEquals(roster, context.Roster)",
    ):
        if required not in roster_source:
            fail(f"lord-promotion exact-target guard missing: {required}")

    flee_finalizer = re.search(
        r"private static Exception FleeFinalizer\(Exception __exception\)(.*?)private static bool RemoveTroopPrefix",
        roster_source,
        re.S,
    )
    if not flee_finalizer:
        fail("could not locate FleeToOtherClanLord finalizer")
    flee_finalizer_body = flee_finalizer.group(1)
    for required in (
        "_currentFlee = context?.Previous;",
        "if (__exception is IndexOutOfRangeException)",
        "return null;",
        "return __exception;",
    ):
        if required not in flee_finalizer_body:
            fail(f"FleeToOtherClanLord boundary guard missing: {required}")
    if flee_finalizer_body.index("_currentFlee = context?.Previous;") > flee_finalizer_body.index("if (__exception is IndexOutOfRangeException)"):
        fail("FleeToOtherClanLord context must be restored before exception suppression")

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

    identity_source = (ROOT / "DSFix" / "PromotionIdentityPatch.cs").read_text(encoding="utf-8")
    for required in (
        'private const string PromotionManagerTypeName = "DistinguishedService.PromotionManager";',
        "context.CreationClaimed = true;",
        "if (context.Source.Race == template.Race)",
        "ReferenceEquals(template, context.Template)",
        "ClampAgeToSourceBodyRange",
        "Campaign.Current.Models.HeroCreationModel.GetBirthAndDeathDay(",
        "_originCharacterField.SetValue(createdCharacter, context.Source)",
        "createdCharacter.Race = context.Source.Race;",
        "SetBodyPropertyRange(createdCharacter, context.Source.BodyPropertyRange);",
        "RestoreOriginalCharacter(context);",
        "PromotionIdentityCampaignBehavior.TrackPromotion(__result);",
    ):
        if required not in identity_source:
            fail(f"promoted-race identity invariant missing: {required}")

    create_hero_guard = re.search(
        r"private static void CreateHeroPrefix\((.*?)private static void CreateHeroPostfix",
        identity_source,
        re.S,
    )
    if not create_hero_guard or "!ReferenceEquals(template, context.Template)" not in create_hero_guard.group(1):
        fail("private HeroCreator.CreateHero hook is not scoped to the exact Distinguished Service wanderer template")

    behavior_source = (ROOT / "DSFix" / "PromotionIdentityCampaignBehavior.cs").read_text(encoding="utf-8")
    for required in (
        "CampaignEvents.OnBeforeSaveEvent.AddNonSerializedListener",
        "CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener",
        "dataStore.SyncData(RaceSaveKey, ref _promotedHeroRaces);",
        "dataStore.SyncData(BodySaveKey, ref _promotedHeroBodyProperties);",
        "CaptureCurrentIdentity(hero);",
        '"BodyPropertyRange"',
    ):
        if required not in behavior_source:
            fail(f"promoted-race save/load persistence missing: {required}")

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
