using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace DSFix
{
    public sealed class SubModule : MBSubModuleBase
    {
        internal const string HarmonyId = "xmarre.dsfix.bannerlord.1.3.15.tor.witm.1.16.distinguishedservice.7.7";
        private Harmony _harmony;
        private AssemblyLoadEventHandler _assemblyLoadHandler;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            _harmony = new Harmony(HarmonyId);
            TryPatchLoadedTargets();

            _assemblyLoadHandler = OnAssemblyLoad;
            AppDomain.CurrentDomain.AssemblyLoad += _assemblyLoadHandler;
        }

        protected override void InitializeGameStarter(Game game, IGameStarter starterObject)
        {
            base.InitializeGameStarter(game, starterObject);

            CampaignGameStarter campaignStarter = starterObject as CampaignGameStarter;
            if (campaignStarter != null)
                campaignStarter.AddBehavior(new PromotionIdentityCampaignBehavior());
        }

        protected override void OnSubModuleUnloaded()
        {
            if (_assemblyLoadHandler != null)
            {
                try { AppDomain.CurrentDomain.AssemblyLoad -= _assemblyLoadHandler; } catch { }
                _assemblyLoadHandler = null;
            }

            try { PromotionIdentityPatch.Reset(); } catch (Exception ex) { DSLog.Write("Failed to clear promoted-troop identity context: " + ex.Message); }
            try { LoreNamePatch.Reset(); } catch (Exception ex) { DSLog.Write("Failed to clear promoted-troop naming context: " + ex.Message); }
            try { _harmony?.UnpatchAll(HarmonyId); } catch { }
            base.OnSubModuleUnloaded();
        }

        private void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            TryPatchLoadedTargets();
        }

        private void TryPatchLoadedTargets()
        {
            if (_harmony == null)
                return;

            try { ShowBattleResultsPatch.TryPatch(_harmony); }
            catch (Exception ex) { DSLog.Write("Patch failed: " + ex, true); }

            try { LordPromotionRosterPatch.TryPatch(_harmony); }
            catch (Exception ex) { DSLog.Write("Lord-promotion roster patch failed: " + ex, true); }

            try { PromotionIdentityPatch.TryPatch(); }
            catch (Exception ex) { DSLog.Write("TOR promoted-troop identity patches were not applied: " + ex, true); }

            try { LoreNamePatch.TryPatch(); }
            catch (Exception ex) { DSLog.Write("TOR promoted-troop naming patches were not applied: " + ex, true); }
        }
    }
}
