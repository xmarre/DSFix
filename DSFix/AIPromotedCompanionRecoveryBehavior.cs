using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;

namespace DSFix
{
    internal sealed class AIPromotedCompanionRecoveryBehavior : CampaignBehaviorBase
    {
        private const string TrackedSaveKey = "DSFix_AIPromotedCompanions_v1";
        private const string LegacyMigrationSaveKey = "DSFix_AIPromotedCompanionLegacyMigration_v1";

        private Dictionary<string, string> _trackedHeroClans = new Dictionary<string, string>();
        private bool _legacyMigrationCompleted;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickHeroEvent.AddNonSerializedListener(this, OnDailyTickHero);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
            CampaignEvents.OnBeforeSaveEvent.AddNonSerializedListener(this, OnBeforeSave);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData(TrackedSaveKey, ref _trackedHeroClans);
            dataStore.SyncData(LegacyMigrationSaveKey, ref _legacyMigrationCompleted);

            if (_trackedHeroClans == null)
                _trackedHeroClans = new Dictionary<string, string>();
        }

        internal static void TrackPromotion(Hero hero, MobileParty party)
        {
            if (!IsExactAIPromotionResult(hero, party) || Campaign.Current == null)
                return;

            try
            {
                AIPromotedCompanionRecoveryBehavior behavior =
                    CampaignBehaviorBase.GetCampaignBehavior<AIPromotedCompanionRecoveryBehavior>();
                behavior?.Track(hero, "exact PromoteToParty handoff");
            }
            catch (Exception ex)
            {
                DSLog.Write("Failed to persist a Distinguished Service AI-promoted companion: " + ex.Message);
            }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            TryLegacyMigration();
            RecoverAllCurrentlyStranded("session launch");
        }

        private void OnDailyTickHero(Hero hero)
        {
            TryLegacyMigration();

            if (!IsTracked(hero))
                return;

            if (!ShouldRemainTracked(hero))
            {
                Untrack(hero);
                return;
            }

            RefreshTrackedClan(hero);

            if (IsStrandedInSettlement(hero))
                TryRecoverToClanParty(hero, null, "daily stranded-companion recovery");
        }

        private void OnMobilePartyDestroyed(MobileParty mobileParty, PartyBase destroyerParty)
        {
            if (mobileParty == null || _trackedHeroClans == null || _trackedHeroClans.Count == 0)
                return;

            // MobilePartyDestroyed is raised before Bannerlord removes the party. Move only exact
            // tracked DS AI companions that still belong to this party. If no safe clan party is
            // currently available, leave the record intact; native cleanup may put the hero in a
            // settlement and the daily recovery path will reattach them later.
            foreach (string heroId in new List<string>(_trackedHeroClans.Keys))
            {
                Hero hero = FindAliveHero(heroId);
                if (hero == null)
                {
                    _trackedHeroClans.Remove(heroId);
                    continue;
                }

                if (!ShouldRemainTracked(hero))
                {
                    _trackedHeroClans.Remove(heroId);
                    continue;
                }

                if (!ReferenceEquals(hero.PartyBelongedTo, mobileParty)
                    || hero.PartyBelongedToAsPrisoner != null)
                {
                    continue;
                }

                RefreshTrackedClan(hero);
                TryRecoverToClanParty(hero, mobileParty, "destroyed-party recovery");
            }
        }

        private void OnBeforeSave()
        {
            if (_trackedHeroClans == null || _trackedHeroClans.Count == 0)
                return;

            foreach (string heroId in new List<string>(_trackedHeroClans.Keys))
            {
                Hero hero = FindAliveHero(heroId);
                if (hero == null || !ShouldRemainTracked(hero))
                    _trackedHeroClans.Remove(heroId);
                else
                    RefreshTrackedClan(hero);
            }
        }

        private void TryLegacyMigration()
        {
            if (_legacyMigrationCompleted || !AIPromotedCompanionRecoveryPatch.IsPatched || Campaign.Current == null)
                return;

            int adopted = 0;
            foreach (Hero hero in Hero.AllAliveHeroes.ToList())
            {
                if (!IsLegacyStrandedCandidate(hero))
                    continue;

                if (Track(hero, "one-time legacy tavern migration"))
                    adopted++;
            }

            _legacyMigrationCompleted = true;
            DSLog.Write(
                "Completed one-time DS AI-companion tavern migration; adopted " + adopted +
                " existing stranded non-player wanderer companion(s).",
                true);
        }

        private void RecoverAllCurrentlyStranded(string reason)
        {
            if (_trackedHeroClans == null || _trackedHeroClans.Count == 0)
                return;

            foreach (string heroId in new List<string>(_trackedHeroClans.Keys))
            {
                Hero hero = FindAliveHero(heroId);
                if (hero == null || !ShouldRemainTracked(hero))
                {
                    _trackedHeroClans.Remove(heroId);
                    continue;
                }

                RefreshTrackedClan(hero);
                if (IsStrandedInSettlement(hero))
                    TryRecoverToClanParty(hero, null, reason);
            }
        }

        private bool Track(Hero hero, string reason)
        {
            if (!ShouldRemainTracked(hero) || string.IsNullOrWhiteSpace(hero.StringId))
                return false;

            if (_trackedHeroClans == null)
                _trackedHeroClans = new Dictionary<string, string>();

            string clanId = hero.CompanionOf != null ? hero.CompanionOf.StringId : string.Empty;
            bool added = !_trackedHeroClans.ContainsKey(hero.StringId);
            _trackedHeroClans[hero.StringId] = clanId ?? string.Empty;

            if (added)
            {
                DSLog.Write(
                    "Tracking Distinguished Service AI-promoted companion '" + hero.StringId +
                    "' for lifecycle recovery (" + reason + ").");
            }

            return added;
        }

        private void Untrack(Hero hero)
        {
            if (hero == null || string.IsNullOrWhiteSpace(hero.StringId) || _trackedHeroClans == null)
                return;

            _trackedHeroClans.Remove(hero.StringId);
        }

        private bool IsTracked(Hero hero)
        {
            return hero != null
                && !string.IsNullOrWhiteSpace(hero.StringId)
                && _trackedHeroClans != null
                && _trackedHeroClans.ContainsKey(hero.StringId);
        }

        private void RefreshTrackedClan(Hero hero)
        {
            if (!IsTracked(hero) || hero.CompanionOf == null)
                return;

            _trackedHeroClans[hero.StringId] = hero.CompanionOf.StringId ?? string.Empty;
        }

        private static bool IsExactAIPromotionResult(Hero hero, MobileParty party)
        {
            if (hero == null
                || party == null
                || party == MobileParty.MainParty
                || hero.CharacterObject == null
                || hero.CharacterObject.Occupation != Occupation.Wanderer
                || hero.CompanionOf == null
                || hero.CompanionOf == Clan.PlayerClan
                || party.LeaderHero == null
                || party.LeaderHero.Clan == null
                || party.LeaderHero.Clan != hero.CompanionOf)
            {
                return false;
            }

            return ReferenceEquals(hero.PartyBelongedTo, party);
        }

        private static bool ShouldRemainTracked(Hero hero)
        {
            return hero != null
                && hero.IsAlive
                && hero.CharacterObject != null
                && hero.CharacterObject.Occupation == Occupation.Wanderer
                && hero.CompanionOf != null
                && hero.CompanionOf != Clan.PlayerClan;
        }

        private static bool IsLegacyStrandedCandidate(Hero hero)
        {
            return ShouldRemainTracked(hero)
                && hero.IsActive
                && hero.PartyBelongedTo == null
                && hero.PartyBelongedToAsPrisoner == null
                && hero.GovernorOf == null
                && hero.CurrentSettlement != null
                && !hero.IsFugitive
                && !hero.IsReleased
                && !hero.IsTraveling;
        }

        private static bool IsStrandedInSettlement(Hero hero)
        {
            return ShouldRemainTracked(hero)
                && hero.IsActive
                && hero.PartyBelongedTo == null
                && hero.PartyBelongedToAsPrisoner == null
                && hero.GovernorOf == null
                && hero.CurrentSettlement != null
                && !hero.IsFugitive
                && !hero.IsReleased
                && !hero.IsTraveling;
        }

        private static void TryRecoverToClanParty(Hero hero, MobileParty excludedParty, string reason)
        {
            if (!ShouldRemainTracked(hero)
                || hero.PartyBelongedToAsPrisoner != null
                || hero.GovernorOf != null)
            {
                return;
            }

            Clan clan = hero.CompanionOf;
            MobileParty targetParty = FindRecoveryParty(hero, clan, excludedParty);
            if (targetParty == null)
                return;

            try
            {
                AddHeroToPartyAction.Apply(hero, targetParty, false);
                DSLog.Write(
                    "Recovered Distinguished Service AI-promoted companion '" + hero.StringId +
                    "' into clan party '" + targetParty.StringId + "' (" + reason + ").",
                    true);
            }
            catch (Exception ex)
            {
                DSLog.Write(
                    "Failed to recover Distinguished Service AI-promoted companion '" + hero.StringId +
                    "' into clan party '" + targetParty.StringId + "': " + ex.Message);
            }
        }

        private static MobileParty FindRecoveryParty(Hero hero, Clan clan, MobileParty excludedParty)
        {
            if (hero == null || clan == null)
                return null;

            IEnumerable<MobileParty> candidates = MobileParty.All.Where(party =>
                party != null
                && party != excludedParty
                && party != MobileParty.MainParty
                && party.IsActive
                && !party.IsDisbanding
                && party.MapEvent == null
                && party.IsLordParty
                && party.LeaderHero != null
                && party.LeaderHero.IsAlive
                && party.LeaderHero.Clan == clan);

            return candidates
                .OrderByDescending(party =>
                    hero.CurrentSettlement != null
                    && ReferenceEquals(party.CurrentSettlement, hero.CurrentSettlement))
                .ThenByDescending(party =>
                    clan.Leader != null
                    && ReferenceEquals(party.LeaderHero, clan.Leader))
                .ThenBy(party => party.StringId ?? string.Empty, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        private static Hero FindAliveHero(string heroId)
        {
            if (string.IsNullOrWhiteSpace(heroId))
                return null;

            return Hero.AllAliveHeroes.FirstOrDefault(hero =>
                hero != null && string.Equals(hero.StringId, heroId, StringComparison.Ordinal));
        }
    }
}
