using TaleWorlds.MountAndBlade;

namespace DSFix
{
    public sealed class InBattleNamingSubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            PromotionNamingPatch.Initialize();
        }

        protected override void OnSubModuleUnloaded()
        {
            PromotionNamingPatch.Reset();
            base.OnSubModuleUnloaded();
        }
    }
}
