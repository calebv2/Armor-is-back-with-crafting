using System;

namespace PlayerArmorMod
{
    internal static class ArmorMaterialPolicy
    {
        internal const int MaximumPrimaryMaterials=32;
        internal const uint LegacyBands=1,SemanticParts=2;
        internal const float MaximumMultiplier=1000000f,MaximumDurability=1000000f;
        internal static bool IsValidMultiplier(float value)
        {return !float.IsNaN(value)&&!float.IsInfinity(value)&&value>0&&value<=MaximumMultiplier;}
        internal static bool IsValidLayout(uint value){return value==LegacyBands||value==SemanticParts;}
        internal static bool IsValidProfile(float average,uint[] hashes)
        {
            if(!IsValidMultiplier(average)||hashes==null||hashes.Length<1||hashes.Length>MaximumPrimaryMaterials)return false;
            foreach(uint hash in hashes)if(hash==0)return false;
            return true;
        }
        internal static float EffectiveMaximum(float configured,bool usesMaterialDurability,bool crafted,bool valid,float average)
        {
            if(float.IsNaN(configured)||float.IsInfinity(configured)||configured<1)return 1;
            if(usesMaterialDurability&&crafted&&(!valid||!IsValidMultiplier(average)))return 1;
            double result=(double)configured*(usesMaterialDurability&&crafted?average:1f);
            return (float)Math.Max(1d,Math.Min(MaximumDurability,result));
        }
    }
}
