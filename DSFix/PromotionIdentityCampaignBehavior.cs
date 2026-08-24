using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace DSFix
{
    internal sealed class PromotionIdentityCampaignBehavior : CampaignBehaviorBase
    {
        private const string RaceSaveKey = "DSFix_PromotedHeroRaces_v1";
        private const string BodySaveKey = "DSFix_PromotedHeroBodyProperties_v1";

        private Dictionary<string, int> _promotedHeroRaces = new Dictionary<string, int>();
        private Dictionary<string, string> _promotedHeroBodyProperties = new Dictionary<string, string>();

        public override void RegisterEvents()
        {
            CampaignEvents.OnBeforeSaveEvent.AddNonSerializedListener(this, OnBeforeSave);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData(RaceSaveKey, ref _promotedHeroRaces);
            dataStore.SyncData(BodySaveKey, ref _promotedHeroBodyProperties);

            if (_promotedHeroRaces == null)
                _promotedHeroRaces = new Dictionary<string, int>();
            if (_promotedHeroBodyProperties == null)
                _promotedHeroBodyProperties = new Dictionary<string, string>();
        }

        internal static void TrackPromotion(Hero hero)
        {
            if (hero == null || Campaign.Current == null)
                return;

            try
            {
                PromotionIdentityCampaignBehavior behavior =
                    CampaignBehaviorBase.GetCampaignBehavior<PromotionIdentityCampaignBehavior>();
                behavior?.CaptureCurrentIdentity(hero);
            }
            catch (Exception ex)
            {
                DSLog.Write("Failed to persist the promoted hero race/body identity: " + ex.Message);
            }
        }

        private void OnBeforeSave()
        {
            if (_promotedHeroRaces == null || _promotedHeroRaces.Count == 0)
                return;

            // Capture the current values instead of permanently forcing the original troop
            // identity. If another supported mechanic intentionally changes a companion later,
            // saving that game makes the new race/body range the value restored next load.
            foreach (string heroId in new List<string>(_promotedHeroRaces.Keys))
            {
                Hero hero = FindAliveHero(heroId);
                if (hero != null)
                    CaptureCurrentIdentity(hero);
            }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            if (_promotedHeroRaces == null || _promotedHeroRaces.Count == 0)
                return;

            foreach (KeyValuePair<string, int> pair in _promotedHeroRaces)
            {
                Hero hero = FindAliveHero(pair.Key);
                if (hero == null || hero.CharacterObject == null)
                    continue;

                try
                {
                    hero.CharacterObject.Race = pair.Value;

                    string bodyPropertyId;
                    if (_promotedHeroBodyProperties != null
                        && _promotedHeroBodyProperties.TryGetValue(pair.Key, out bodyPropertyId)
                        && !string.IsNullOrWhiteSpace(bodyPropertyId))
                    {
                        MBBodyProperty bodyPropertyRange =
                            Game.Current?.ObjectManager?.GetObject<MBBodyProperty>(bodyPropertyId);
                        if (bodyPropertyRange == null)
                        {
                            DSLog.Write(
                                "Could not restore promoted hero body range '" + bodyPropertyId +
                                "' for " + pair.Key + "; the saved race was restored and the body range was left unchanged.");
                        }
                        else if (!ReflectionUtil.WriteMember(
                            hero.CharacterObject,
                            "BodyPropertyRange",
                            bodyPropertyRange))
                        {
                            DSLog.Write(
                                "Could not restore promoted hero BodyPropertyRange for " + pair.Key +
                                "; the saved race was restored and the body range was left unchanged.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    DSLog.Write("Failed to restore promoted hero race/body identity for " + pair.Key + ": " + ex.Message);
                }
            }
        }

        private void CaptureCurrentIdentity(Hero hero)
        {
            if (hero?.CharacterObject == null || string.IsNullOrWhiteSpace(hero.StringId))
                return;

            string heroId = hero.StringId;
            _promotedHeroRaces[heroId] = hero.CharacterObject.Race;

            MBBodyProperty bodyPropertyRange = hero.CharacterObject.BodyPropertyRange;
            if (bodyPropertyRange != null && !string.IsNullOrWhiteSpace(bodyPropertyRange.StringId))
                _promotedHeroBodyProperties[heroId] = bodyPropertyRange.StringId;
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
