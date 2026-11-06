using Alta;

namespace PlayerArmorMod
{
    internal static class ArmorWoodPolicy
    {
        internal static float DurabilityMultiplier(PhysicalMaterial material)
        {
            if(material==null)return 1f;
            var settings=ArmorSettings.GetWoodMaterial(material.name);
            if(settings!=null)return settings.durabilityMultiplier;
            float native=material.DurabilityMultiplier;
            return ArmorMaterialPolicy.IsValidMultiplier(native)?native:1f;
        }

        internal static float EffectiveReduction(ArmorPickup pickup,float configured)
        {
            if(pickup.Style!=ArmorStyle.Woodland)return configured;
            var state=pickup.GetComponent<ArmorMaterialState>();
            uint[] woods;
            uint layout;
            if(state==null||!state.ReadOldCraftPalette(out woods,out layout)||woods==null||woods.Length==0)return configured;

            // A Woodland palette contains primary wood sections in craft order.
            // Every wedge contributes once, including repeated species.
            double total=0d;
            foreach(uint hash in woods)
            {
                var material=HashedGeneralValue<PhysicalMaterial>.Get(hash);
                var settings=material==null?null:ArmorSettings.GetWoodMaterial(material.name);
                total+=settings==null?1f:settings.damageReductionMultiplier;
            }
            return configured*(float)(total/woods.Length);
        }
    }
}
