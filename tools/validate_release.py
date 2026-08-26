from __future__ import annotations

import argparse
import pathlib
import re
import sys
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
EXPECTED_VERSION = "1.7.7"


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
        "MapEventEndedPrefix",
        "MapEventEndedFinalizer",
        "RemoveTroopPrefix",
        "RemoveTroopFinalizer",
        "MatchesProtectedCleanupTarget",
        "MatchesCurrentFleeTarget",
        "MatchesCurrentMapEventRoster",
        'ReflectionUtil.ReadMember(__0, "Troops")',
        'ReflectionUtil.ReadMember(mapEvent, "Parties")',
        "PartiesOnSide",
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
        'private const string MapEventTypeName = "TaleWorlds.CampaignSystem.MapEvents.MapEvent";',
        'private const string MapEventPartyTypeName = "TaleWorlds.CampaignSystem.MapEvents.MapEventParty";',
        "FindMapEventEnded(managerType)",
        "harmony.Patch(mapEventEnded",
        "private static Exception RemoveTroopFinalizer",
        "__exception is IndexOutOfRangeException",
        "MatchesProtectedCleanupTarget(__instance, __0)",
        "ReferenceEquals(troop, context.Wanderer)",
        "ReferenceEquals(roster, context.Roster)",
        "ReferenceEquals(roster, context.Rosters[i])",
        "ReflectionUtil.TypeNameEquals(party.GetType(), MapEventPartyTypeName)",
    ):
        if required not in roster_source:
            fail(f"post-map-event roster guard missing: {required}")

    flee_finalizer = re.search(
        r"private static Exception FleeFinalizer\(Exception __exception\)(.*?)private static void MapEventEndedPrefix",
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

    map_event_scope = re.search(
        r"private static void MapEventEndedPrefix\(object __0\)(.*?)private static bool RemoveTroopPrefix",
        roster_source,
        re.S,
    )
    if not map_event_scope:
        fail("could not locate MapEventEnded cleanup scope")
    map_event_scope_body = map_event_scope.group(1)
    for required in (
        "Previous = _currentMapEvent",
        "Rosters = CollectMapEventRosters(__0)",
        "_currentMapEvent = context;",
        "_currentMapEvent = context?.Previous;",
        "return __exception;",
    ):
        if required not in map_event_scope_body:
            fail(f"MapEventEnded cleanup scope missing: {required}")

    remove_finalizer = re.search(
        r"private static Exception RemoveTroopFinalizer\(Exception __exception, object __instance, object __0\)(.*?)private static bool MatchesProtectedCleanupTarget",
        roster_source,
        re.S,
    )
    if not remove_finalizer:
        fail("could not locate protected RemoveTroop finalizer")
    remove_finalizer_body = remove_finalizer.group(1)
    if "!MatchesProtectedCleanupTarget(__instance, __0)" not in remove_finalizer_body:
        fail("RemoveTroop IndexOutOfRangeException suppression is not scoped to Distinguished Service post-map-event cleanup")
    if "return null;" not in remove_finalizer_body or "return __exception;" not in remove_finalizer_body:
        fail("RemoveTroop finalizer must suppress only matched failures and propagate everything else")

    protected_matcher = re.search(
        r"private static bool MatchesProtectedCleanupTarget\(object roster, object troop\)(.*?)private static bool MatchesCurrentFleeTarget",
        roster_source,
        re.S,
    )
    if not protected_matcher:
        fail("could not locate protected cleanup matcher")
    if "MatchesCurrentFleeTarget(roster, troop) || MatchesCurrentMapEventRoster(roster)" not in protected_matcher.group(1):
        fail("protected cleanup matcher does not cover both exact flee and exact ended-map-event rosters")

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
