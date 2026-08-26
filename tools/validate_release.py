from __future__ import annotations

import argparse
import pathlib
import re
import sys
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
EXPECTED_VERSION = "1.7.8"


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
        "MapEventEndedTranspiler",
        "FleeToOtherClanLordTranspiler",
        "ExpectedMapEventEndedRewriteCount = 2",
        "ExpectedFleeRewriteCount = 3",
        "RemoveTroopIfPresent",
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
        "FindFleeToOtherClanLord(managerType)",
        "FindRemoveTroop(troopRosterType)",
        "harmony.Patch(flee",
        "harmony.Patch(mapEventEnded",
        "transpiler: new HarmonyMethod",
        "ExpectedMapEventEndedRewriteCount = 2",
        "ExpectedFleeRewriteCount = 3",
        "RewriteRemoveTroopCalls",
        "instruction.Calls(_removeTroopMethod)",
        "instruction.opcode = OpCodes.Call;",
        "instruction.operand = SafeRemoveTroopMethod;",
        "if (rewriteCount != expectedCount)",
        "Refusing to apply a partial or structurally mismatched Distinguished Service compatibility rewrite.",
        "harmony.Unpatch(flee, HarmonyPatchType.Transpiler, harmony.Id);",
        "harmony.Unpatch(mapEventEnded, HarmonyPatchType.Transpiler, harmony.Id);",
        "private static void RemoveTroopIfPresent(TroopRoster roster, CharacterObject troop, int numberToRemove, UniqueTroopDescriptor troopSeed, int xp)",
        "if (roster.GetTroopCount(troop) <= 0)",
        "roster.RemoveTroop(troop, numberToRemove, troopSeed, xp);",
    ):
        if required not in roster_source:
            fail(f"exact Distinguished Service RemoveTroop rewrite missing: {required}")

    # The root-cause fix must not regress to a global TroopRoster patch, event-wide context,
    # or exception suppression. Only the five call sites in the two DS methods are rewritten.
    for forbidden in (
        "harmony.Patch(removeTroop",
        "RemoveTroopPrefix",
        "RemoveTroopFinalizer",
        "FleeFinalizer",
        "MapEventEndedPrefix",
        "MapEventEndedFinalizer",
        "MatchesProtectedCleanupTarget",
        "MatchesCurrentMapEventRoster",
        "CollectMapEventRosters",
        "[ThreadStatic]",
        "IndexOutOfRangeException",
    ):
        if forbidden in roster_source:
            fail(f"broad/exception-based post-map-event workaround reintroduced: {forbidden}")

    patch_scope = re.search(
        r"try\s*\{(.*?)\}\s*catch\s*\{(.*?)\}\s*\n\s*_patched = true;",
        roster_source,
        re.S,
    )
    if not patch_scope:
        fail("could not locate atomic Distinguished Service patch application scope")
    patch_try, patch_catch = patch_scope.groups()
    for required in (
        "harmony.Patch(flee",
        "nameof(FleeToOtherClanLordTranspiler)",
        "harmony.Patch(mapEventEnded",
        "nameof(MapEventEndedTranspiler)",
    ):
        if required not in patch_try:
            fail(f"atomic patch try block missing: {required}")
    for required in (
        "harmony.Unpatch(flee, HarmonyPatchType.Transpiler, harmony.Id);",
        "harmony.Unpatch(mapEventEnded, HarmonyPatchType.Transpiler, harmony.Id);",
        "throw;",
    ):
        if required not in patch_catch:
            fail(f"atomic patch rollback missing: {required}")

    rewrite_helper = re.search(
        r"private static IEnumerable<CodeInstruction> RewriteRemoveTroopCalls\((.*?)private static void RemoveTroopIfPresent",
        roster_source,
        re.S,
    )
    if not rewrite_helper:
        fail("could not locate exact RemoveTroop rewrite helper")
    rewrite_body = rewrite_helper.group(1)
    if "rewriteCount++" not in rewrite_body or "rewriteCount != expectedCount" not in rewrite_body:
        fail("RemoveTroop transpiler does not enforce the exact target call count")
    if "InvalidOperationException" not in rewrite_body:
        fail("RemoveTroop transpiler must fail closed on a target binary shape mismatch")

    safe_remove = re.search(
        r"private static void RemoveTroopIfPresent\((.*?)\n        }\n    }\n}",
        roster_source,
        re.S,
    )
    if not safe_remove:
        fail("could not locate safe RemoveTroop replacement")
    safe_remove_body = safe_remove.group(1)
    if "roster.GetTroopCount(troop) <= 0" not in safe_remove_body:
        fail("safe replacement does not require a positive live troop count")
    if "roster.RemoveTroop(troop, numberToRemove, troopSeed, xp);" not in safe_remove_body:
        fail("safe replacement does not preserve native removal when the troop is present")

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
