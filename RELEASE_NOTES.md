## DSFix v1.7.4

Fixes the promoted-troop naming patch reporting:

`System.MissingMethodException: GenerateHeroFirstName(Hero)`

when starting a Bannerlord 1.3.15 campaign.

The failure was in DSFix's reflection target lookup, not in Bannerlord: `TaleWorlds.CampaignSystem.NameGenerator.GenerateHeroFirstName(Hero)` is an **instance method** in Bannerlord 1.3.15, while v1.7.3 incorrectly searched only static methods. v1.7.4 binds the Harmony patch to the actual instance method and adds release validation so this target cannot regress to a static lookup.

The v1.7.3 optional `get_using_extern_namelist()` compatibility path remains intact. The TOR summoned-agent post-battle fix, culture-accurate promoted naming, pre-inquiry name enforcement, and exact-target `FleeToOtherClanLord` roster crash guard are unchanged.
