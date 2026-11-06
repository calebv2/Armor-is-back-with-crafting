using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace PlayerArmorMod
{
    internal sealed class ArmorRecipeHistory
    {public int schemaVersion=1;public ArmorRecipeHistoryEntry[] versions=null;}
    internal sealed class ArmorRecipeHistoryEntry
    {public uint hash=0;public string descriptor=null;}
    internal static class ArmorCraftingRecipeCodec
    {
        internal const uint Sentinel=65535;
        internal const int MaximumDescriptorBytes=4096;
        internal static string Canonical(ArmorRecipeSettings recipe)
        {
            ArmorCraftingPlan.Count(recipe);
            ArmorConstructionPlan.Validate(recipe);
            if(!ArmorCatalog.RecipePagePieces.Any(p=>p.PrefabName==recipe.prefabName)||recipe.ingredients.Length>8)
                throw new InvalidDataException("Unknown armor recipe result or excessive ingredient kinds");
            var normalized=new ArmorRecipeSettings{prefabName=recipe.prefabName,layoutVersion=recipe.layoutVersion,ingredients=recipe.ingredients.Select(i=>new ArmorIngredientSettings
                {code=Normalize(i.code),material=string.IsNullOrEmpty(i.material)?null:Normalize(i.material),quantity=i.quantity})
                .OrderBy(i=>i.code,StringComparer.Ordinal).ToArray()};
            if(normalized.ingredients.Select(i=>i.code).Distinct(StringComparer.Ordinal).Count()!=normalized.ingredients.Length)
                throw new InvalidDataException("Duplicate normalized armor ingredient");
            string json=JsonConvert.SerializeObject(normalized,Formatting.None);
            if(Encoding.UTF8.GetByteCount(json)>MaximumDescriptorBytes)throw new InvalidDataException("Armor recipe descriptor is too large");
            return json;
        }
        internal static ArmorRecipeSettings Parse(string descriptor)
        {
            if(string.IsNullOrEmpty(descriptor)||Encoding.UTF8.GetByteCount(descriptor)>MaximumDescriptorBytes)throw new InvalidDataException("Invalid armor recipe descriptor size");
            var recipe=JsonConvert.DeserializeObject<ArmorRecipeSettings>(descriptor,new JsonSerializerSettings{MissingMemberHandling=MissingMemberHandling.Error,MaxDepth=6});
            if(Canonical(recipe)!=descriptor)throw new InvalidDataException("Armor recipe descriptor must use canonical form");
            return recipe;
        }
        internal static uint AllocateHash(string descriptor,Func<uint,bool> occupied)
        {
            Parse(descriptor);
            for(int salt=0;salt<65535;salt++)
            {uint hash=ArmorCraftingPlan.Hash16("PlayerArmor/RecipeVersion/"+descriptor+"/"+salt);if(hash!=Sentinel&&!occupied(hash))return hash;}
            throw new InvalidOperationException("No unclaimed native 16-bit hash remains for armor recipe version");
        }
        internal static ArmorRecipeHistory ReadHistory(string json)
        {
            if(string.IsNullOrEmpty(json)||Encoding.UTF8.GetByteCount(json)>4*1024*1024)throw new InvalidDataException("Invalid armor recipe history size");
            var history=JsonConvert.DeserializeObject<ArmorRecipeHistory>(json,new JsonSerializerSettings{MissingMemberHandling=MissingMemberHandling.Error,MaxDepth=8});
            ValidateHistory(history);return history;
        }
        internal static string WriteHistory(ArmorRecipeHistory history)
        {
            ValidateHistory(history);string json=JsonConvert.SerializeObject(history,Formatting.Indented);
            if(Encoding.UTF8.GetByteCount(json)>4*1024*1024)throw new InvalidDataException("Armor recipe history exceeds 4 MiB");return json;
        }
        static void ValidateHistory(ArmorRecipeHistory history)
        {
            if(history==null||history.schemaVersion!=1||history.versions==null||history.versions.Length>2048)throw new InvalidDataException("Invalid recipe-history.json; preserve the archive while old recipe pages/projects exist");
            var seen=new HashSet<uint>();
            foreach(var item in history.versions)
            {
                if(item==null||item.hash==0||item.hash>=Sentinel||!seen.Add(item.hash))throw new InvalidDataException("Invalid or duplicate archived armor recipe hash");
                Parse(item.descriptor);
            }
        }
        static string Normalize(string text)
        {
            if(string.IsNullOrEmpty(text)||text.Length>64)throw new InvalidDataException("Invalid armor ingredient/material identifier");
            foreach(char c in text)if(c>127||(!char.IsLetterOrDigit(c)&&c!='_'))throw new InvalidDataException("Armor ingredient/material identifiers must be ASCII letters, digits or underscores");
            uint number;if(uint.TryParse(text,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out number))
            {if(number==0)throw new InvalidDataException("Armor ingredient hash must be positive");return number.ToString(System.Globalization.CultureInfo.InvariantCulture);}
            return text.Replace("_","").ToLowerInvariant();
        }
    }
}
