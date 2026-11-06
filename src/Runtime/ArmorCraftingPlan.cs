using System;
using System.Collections.Generic;

namespace PlayerArmorMod
{
    // A stable schema shared by the native recipe registration and its tests.
    // Hashes fit the game's native 16-bit hashed-value stream, unlike network
    // prefab identifiers. Page prefab names stay fixed while recipe contents
    // receive immutable version identities in ArmorCraftingRecipeCodec.
    internal static class ArmorCraftingPlan
    {
        internal const int MaximumPieces=32;
        internal static uint GlyphHash(string prefabName)
        {
            // Keep all published glyph identities. The fifth family uses its
            // own stable namespace: the unsalted right bracer would collide
            // with an existing native glyph in the game's 16-bit domain.
            string prefix=prefabName!=null&&prefabName.StartsWith("ATT_MushroomArmor_",StringComparison.Ordinal)
                ?"PlayerArmor/Glyph/Mushroom/v1/":"PlayerArmor/Glyph/";
            return Hash16(prefix+prefabName);
        }
        internal static uint Hash16(string value)
        {
            unchecked{uint hash=2166136261;foreach(char c in value){hash^=c;hash*=16777619;}return (hash%65535)+1;}
        }
        internal static string PageName(ArmorPieceDefinition definition)
        {return "ATT_ArmorRecipe_"+(definition.Style==ArmorStyle.Metal?"Iron_":definition.Style+"_")+definition.PrefabName.Substring(definition.PrefabName.IndexOf("Armor_",StringComparison.Ordinal)+6);}
        internal static int Count(ArmorRecipeSettings recipe)
        {
            if(recipe==null||recipe.ingredients==null||recipe.ingredients.Length==0)throw new InvalidOperationException("An armor recipe needs ingredients");
            int count=0;var codes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var ingredient in recipe.ingredients)
            {
                if(ingredient==null||string.IsNullOrEmpty(ingredient.code)||ingredient.quantity<1||!codes.Add(ingredient.code))
                    throw new InvalidOperationException("Invalid or duplicate armor ingredient");
                checked{count+=ingredient.quantity;}
            }
            if(count>MaximumPieces)throw new InvalidOperationException("Armor workbench recipes support at most "+MaximumPieces+" physical ingredient pieces");
            return count;
        }
        internal static bool MaterialMatches(string required,string actual)
        {
            if(string.IsNullOrEmpty(required))return true;
            if(string.IsNullOrEmpty(actual))return false;
            if(string.Equals(required,"Leather",StringComparison.OrdinalIgnoreCase))return actual.IndexOf("Leather",StringComparison.OrdinalIgnoreCase)>=0;
            return string.Equals(Compact(required),Compact(actual),StringComparison.OrdinalIgnoreCase);
        }
        static string Compact(string value){return value.Replace(" ","").Replace("_","");}
    }
}
