using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.SaveSystem;

namespace BanditMilitias
{
    public class BanditMilitiasSaveableTypeDefiner : SaveableTypeDefiner
    {
        public BanditMilitiasSaveableTypeDefiner() : base(42069)
        {
        }

        protected override void DefineClassTypes()
        {
            base.DefineClassTypes();
            AddClassDefinition(typeof(ModBanditMilitiaPartyComponent), 42069);
        }

        protected override void DefineContainerDefinitions()
        {
            base.DefineContainerDefinitions();
            ConstructContainerDefinition(typeof(Dictionary<Hero, float>));
        }
    }
}