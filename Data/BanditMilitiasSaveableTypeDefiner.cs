using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BanditMilitiasRedux.Data;

// Released save ids: append only, never reorder or change.
public class BanditMilitiasSaveableTypeDefiner() : SaveableTypeDefiner(SaveBaseId)
{
    private const int SaveBaseId = 4320639;
    private const int PartyComponentSaveId = 4320639;

    protected override void DefineClassTypes()
    {
        base.DefineClassTypes();
        AddClassDefinition(typeof(BanditMilitiaPartyComponent), PartyComponentSaveId);
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
