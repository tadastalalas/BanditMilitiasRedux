using System.Collections.Generic;
using BanditMilitiasRedux.Constructors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BanditMilitiasRedux
{
    public class BanditMilitiasSaveableTypeDefiner() : SaveableTypeDefiner(4320639)
    {
        protected override void DefineClassTypes()
        {
            base.DefineClassTypes();
            AddClassDefinition(typeof(BanditMilitiaPartyComponent), 4320639);
        }

        protected override void DefineContainerDefinitions()
        {
            base.DefineContainerDefinitions();
            ConstructContainerDefinition(typeof(Dictionary<Hero, float>));
            ConstructContainerDefinition(typeof(List<Hero>));
            ConstructContainerDefinition(typeof(Dictionary<Clan, List<Hero>>));
            ConstructContainerDefinition(typeof(Dictionary<Hero, int>));
            ConstructContainerDefinition(typeof(Dictionary<Hero, CampaignTime>));
            ConstructContainerDefinition(typeof(Dictionary<Hero, Dictionary<Hero, int>>));
        }
    }
}