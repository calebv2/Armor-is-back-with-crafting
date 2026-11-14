using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace PlayerArmorMod
{
    public sealed class ArmorPieceSettings
    {
        public string prefabName;
        public float damageReduction,maximumDurability,wearPerAbsorbedDamage;
        [JsonProperty(NullValueHandling=NullValueHandling.Ignore)] public float? projectileDamageReduction;
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public float movementPenalty;
    }
    public sealed class ArmorWoodMaterialSettings
    {
        public string material;
        public float durabilityMultiplier=1f,damageReductionMultiplier=1f;
    }
    public sealed class ArmorIngredientSettings
    {
        public string code;
        public string material;
        public int quantity;
    }
    public sealed class ArmorRecipeSettings
    {
        public string prefabName;
        public ArmorIngredientSettings[] ingredients;
        // Absent means the historical sliced layout. Omit null so archived
        // canonical descriptors retain their exact original bytes and hashes.
        [JsonProperty(NullValueHandling=NullValueHandling.Ignore)]
        public int? layoutVersion;
    }
    internal static class ArmorConstructionPlan
    {
        internal const int WoodlandBucketLayout=4;
        internal static int PrimaryCount(ArmorStyle style,ArmorSlot slot)
        {
            if(slot==ArmorSlot.Hips)return 4;
            if(slot==ArmorSlot.Head)return style==ArmorStyle.Leather?2:3;
            // Fungal chest courses are three complete overlapping cap shells;
            // each open forearm guard is one complete shell with its own strap.
            if(style==ArmorStyle.Mushroom)
                return slot==ArmorSlot.Chest?3:slot==ArmorSlot.LeftForearm||slot==ArmorSlot.RightForearm?1:2;
            if(style==ArmorStyle.Leather&&(slot==ArmorSlot.LeftForearm||slot==ArmorSlot.RightForearm))return 1;
            return 2;
        }
        internal static int FastenerCount(ArmorStyle style,ArmorSlot slot)
        {return style==ArmorStyle.Leather&&(slot==ArmorSlot.LeftForearm||slot==ArmorSlot.RightForearm)?2:1;}
        internal static uint PrimarySource(ArmorStyle style)
        // Mushroom uses the native leather-roll workbench component for its
        // cured shell regions. Food mushrooms are not native workbench parts.
        {return style==ArmorStyle.Woodland?47118u:ArmorCatalog.UsesMetalMaterials(style)?50988u:47760u;}
        internal static uint FastenerSource(ArmorStyle style)
        {return style==ArmorStyle.Woodland?43836u:(style==ArmorStyle.Metal||style==ArmorStyle.Forgeguard)?62594u:63204u;}
        internal static uint Layout(ArmorRecipeSettings recipe)
        {
            int value=recipe.layoutVersion??1;
            if(value!=1&&value!=2&&value!=3&&
                (value!=WoodlandBucketLayout||recipe.prefabName!=ArmorCatalog.WoodlandPieces[0].PrefabName))
                throw new InvalidDataException("Unsupported armor recipe layoutVersion");
            return (uint)value;
        }
        // Jig staging changes the project, not the saved material-region format.
        internal static uint MaterialLayout(ArmorRecipeSettings recipe)
        {return Math.Min(2u,Layout(recipe));}
        internal static void Validate(ArmorRecipeSettings recipe)
        {
            if(Layout(recipe)<2)return;
            var piece=ArmorCatalog.AllPieces.FirstOrDefault(p=>p.PrefabName==recipe.prefabName);
            if(piece==null)throw new InvalidDataException("Unknown armor construction result");
            string main=PrimarySource(piece.Style).ToString(System.Globalization.CultureInfo.InvariantCulture),fastener=FastenerSource(piece.Style).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if(recipe.ingredients==null||recipe.ingredients.Length!=2
                ||!recipe.ingredients.Any(i=>i!=null&&i.code==main&&i.quantity==PrimaryCount(piece.Style,piece.Slot))
                ||!recipe.ingredients.Any(i=>i!=null&&i.code==fastener&&i.quantity==FastenerCount(piece.Style,piece.Slot)))
                throw new InvalidDataException("Authored construction needs its complete panels and fasteners for "+recipe.prefabName+"; use layoutVersion 1 for a custom historical recipe");
        }
    }
    public sealed class ArmorConfiguration
    {
        public int schemaVersion=1;
        public bool protectionEnabled=true,durabilityEnabled=true,cosmeticCompatibilityEnabled=true,craftingEnabled=true;
        public float mushroomChestChancePercent=10f;
        public ArmorWoodMaterialSettings[] woodMaterials;
        public ArmorPieceSettings[] pieces;
        public ArmorRecipeSettings[] recipes;
    }
    internal static class ArmorSettings
    {
        internal const int MaximumJsonBytes=65536;
        const int WireSchemaVersion=1;
        const int ServerSchemaVersion=3;
        const float CrystalWyrmDamageReduction=.30f;
        const float CrystalWyrmDurability=250f;
        internal static ArmorConfiguration Current {get;private set;}=Defaults();
        internal static uint Revision {get;private set;}=1;
        internal static bool RemoteReady {get;private set;}
        internal static event Action Changed;
        internal static string ConfigurationPath {get;private set;}
        static float nextPoll;static string lastError,lastAcceptedSource;
        static ArmorRecipeSettings[] activeRecipes;
        internal static void FreezeRecipes(){activeRecipes=Current.recipes;}
        internal static ArmorPieceSettings GetPiece(ArmorStyle style,ArmorSlot slot)
        {string name=ArmorCatalog.Get(style,slot).PrefabName;return Current.pieces.First(p=>p.prefabName==name);}
        internal static ArmorWoodMaterialSettings GetWoodMaterial(string material)
        {
            return Current.woodMaterials.FirstOrDefault(entry=>string.Equals(entry.material,material,StringComparison.OrdinalIgnoreCase));
        }
        internal static ArmorRecipeSettings GetRecipe(string prefabName)
        {return Current.recipes.FirstOrDefault(p=>p.prefabName==prefabName);}
        // Chest drops still need the historical geometry and ingredient layout
        // for disassembly and saved projects. This path never advertises a recipe.
        internal static ArmorRecipeSettings ConstructionFor(ArmorPieceDefinition piece)
        {return ArmorCatalog.IsChestOnly(piece.Style)?DefaultConstruction(piece):GetRecipe(piece.PrefabName);}
        internal static string Serialize(ArmorStyle latestStyle=ArmorCatalog.LatestStyle)
        {
            // The chest roll and wood balance are evaluated on the server.
            // Older clients reject unknown equipment settings fields.
            var wire=Newtonsoft.Json.Linq.JObject.FromObject(Current);
            wire[nameof(ArmorConfiguration.schemaVersion)]=WireSchemaVersion;
            wire.Remove(nameof(ArmorConfiguration.mushroomChestChancePercent));
            wire.Remove(nameof(ArmorConfiguration.woodMaterials));
            if(latestStyle<ArmorCatalog.LatestStyle)
            {
                var pieces=(Newtonsoft.Json.Linq.JArray)wire[nameof(ArmorConfiguration.pieces)];
                var unsupported=new HashSet<string>(ArmorCatalog.AllPieces.Where(p=>p.Style>latestStyle).Select(p=>p.PrefabName),StringComparer.Ordinal);
                foreach(var piece in pieces.Where(p=>unsupported.Contains((string)p[nameof(ArmorPieceSettings.prefabName)])).ToArray())
                    piece.Remove();
                var recipes=(Newtonsoft.Json.Linq.JArray)wire[nameof(ArmorConfiguration.recipes)];
                foreach(var recipe in recipes.Where(p=>unsupported.Contains((string)p[nameof(ArmorRecipeSettings.prefabName)])).ToArray())
                    recipe.Remove();
                if(latestStyle<ArmorStyle.Forgeguard)
                    foreach(Newtonsoft.Json.Linq.JObject piece in pieces)
                    {piece.Remove(nameof(ArmorPieceSettings.projectileDamageReduction));piece.Remove(nameof(ArmorPieceSettings.movementPenalty));}
            }
            return wire.ToString(Formatting.None);
        }
        internal static void InitializeServer(string userData)
        {
            ConfigurationPath=Path.Combine(userData,"PlayerArmor","armor.json");
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigurationPath));
            if(!File.Exists(ConfigurationPath))File.WriteAllText(ConfigurationPath,JsonConvert.SerializeObject(Defaults(),Formatting.Indented));
            Poll(0,true);
        }
        internal static void Poll(float now,bool force=false)
        {
            if(ConfigurationPath==null||(!force&&now<nextPoll))return;nextPoll=now+3;
            try
            {
                var info=new FileInfo(ConfigurationPath);if(!info.Exists)throw new InvalidDataException("armor.json is missing; retaining the last accepted settings");
                if(info.Length>MaximumJsonBytes)throw new InvalidDataException("armor.json exceeds 64 KiB");
                string source=File.ReadAllText(ConfigurationPath);
                if(!force&&source==lastAcceptedSource){lastError=null;return;}
                var accepted=Parse(source);
                if(accepted.schemaVersion<ServerSchemaVersion&&!UpgradeServerConfiguration(accepted,ref source))
                    return;
                if(activeRecipes!=null&&JsonConvert.SerializeObject(accepted.recipes)!=JsonConvert.SerializeObject(activeRecipes))
                {accepted.recipes=activeRecipes;Core.Log("PLAYER_ARMOR_RECIPES_PENDING_RESTART recipes remain unchanged until restart; live balance and cosmetic settings accepted");}
                Current=accepted;lastAcceptedSource=source;Revision++;lastError=null;
                Notify();Core.Log("PLAYER_ARMOR_CONFIG_ACCEPTED revision="+Revision+" path="+ConfigurationPath);
            }
            catch(Exception error)
            {string message=error.GetBaseException().Message;if(message!=lastError){lastError=message;Core.Error("PLAYER_ARMOR_CONFIG_REJECTED retaining previous settings: "+message);}}
        }
        static bool UpgradeServerConfiguration(ArmorConfiguration accepted,ref string source)
        {
            var original=Newtonsoft.Json.Linq.JObject.Parse(source);
            var existing=new HashSet<string>((original["pieces"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray())
                .Where(p=>p!=null).Select(p=>(string)p["prefabName"]).Where(n=>n!=null),StringComparer.Ordinal);
            foreach(var piece in accepted.pieces)
            {
                if(!existing.Contains(piece.prefabName))continue;
                if(accepted.schemaVersion==WireSchemaVersion&&piece.prefabName.StartsWith(ArmorCatalog.CrystalWyrmPrefabPrefix,StringComparison.Ordinal)
                    &&piece.damageReduction==.18f&&piece.maximumDurability==250f)
                {piece.damageReduction=CrystalWyrmDamageReduction;piece.maximumDurability=500f;}
                piece.maximumDurability=Math.Max(1f,piece.maximumDurability*.5f);
            }
            accepted.schemaVersion=ServerSchemaVersion;
            var updated=JsonConvert.SerializeObject(accepted,Formatting.Indented);
            if(System.Text.Encoding.UTF8.GetByteCount(updated)>MaximumJsonBytes)
                updated=JsonConvert.SerializeObject(accepted,Formatting.None);
            if(System.Text.Encoding.UTF8.GetByteCount(updated)>MaximumJsonBytes)
            {
                Core.Error("PLAYER_ARMOR_CONFIG_UPGRADE_NOT_SAVED updated settings exceed 64 KiB; applied in memory");
                return true;
            }
            var temporary=ConfigurationPath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllText(temporary,updated);
                if(File.ReadAllText(ConfigurationPath)!=source)return false;
                File.Replace(temporary,ConfigurationPath,ConfigurationPath+".pre-durability-halving.bak");
                source=updated;
            }
            catch(Exception error) when(error is IOException||error is UnauthorizedAccessException)
            {
                Core.Error("PLAYER_ARMOR_CONFIG_UPGRADE_NOT_SAVED updated settings applied in memory: "+error.Message);
            }
            finally
            {
                try{File.Delete(temporary);}
                catch(Exception error) when(error is IOException||error is UnauthorizedAccessException)
                {Core.Error("PLAYER_ARMOR_CONFIG_TEMP_CLEANUP_FAILED "+error.Message);}
            }
            return true;
        }
        internal static void ApplyRemote(string json)
        {
            var accepted=Parse(json);Current=accepted;Revision++;RemoteReady=true;
            Notify();
        }
        internal static void ResetRemote()
        {Current=Defaults();RemoteReady=false;Revision++;Notify();}
        static void Notify()
        {var changed=Changed;if(changed==null)return;foreach(Action handler in changed.GetInvocationList())try{handler();}catch(Exception error){Core.Error("PLAYER_ARMOR_SETTINGS_LISTENER_FAILED "+error.GetBaseException().Message);}}
        internal static ArmorConfiguration Parse(string json)
        {
            if(string.IsNullOrWhiteSpace(json)||System.Text.Encoding.UTF8.GetByteCount(json)>MaximumJsonBytes)throw new InvalidDataException("Invalid armor settings size");
            var value=JsonConvert.DeserializeObject<ArmorConfiguration>(json,new JsonSerializerSettings{MissingMemberHandling=MissingMemberHandling.Error,MaxDepth=12});
            if(value==null||(value.schemaVersion!=WireSchemaVersion&&value.schemaVersion!=2&&value.schemaVersion!=ServerSchemaVersion))
                throw new InvalidDataException("Expected armor settings schemaVersion 1, 2 or 3");
            if(!Finite(value.mushroomChestChancePercent)||value.mushroomChestChancePercent<0||value.mushroomChestChancePercent>100)
                throw new InvalidDataException("mushroomChestChancePercent must be between 0 and 100");
            var defaults=Defaults();if(value.pieces==null)value.pieces=defaults.pieces;if(value.recipes==null)value.recipes=defaults.recipes;
            if(value.woodMaterials==null)value.woodMaterials=defaults.woodMaterials;
            if(value.woodMaterials.Length>64)throw new InvalidDataException("woodMaterials allows at most 64 entries");
            var woods=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var wood in value.woodMaterials)
            {
                if(wood==null||string.IsNullOrWhiteSpace(wood.material)||wood.material.Length>64||wood.material.Trim()!=wood.material
                    ||wood.material.Any(char.IsControl)||!woods.Add(wood.material)||!Finite(wood.durabilityMultiplier)
                    ||wood.durabilityMultiplier<.01f||wood.durabilityMultiplier>10f||!Finite(wood.damageReductionMultiplier)
                    ||wood.damageReductionMultiplier<0f||wood.damageReductionMultiplier>10f)
                    throw new InvalidDataException("Invalid or duplicate wood material balance entry");
            }
            // Existing administrators keep their fourteen original entries,
            // including custom costs/material rules. Only missing NEW families
            // receive defaults; missing original entries remain an error.
            var addedNames=new HashSet<string>(ArmorCatalog.ExpansionPieces.Select(p=>p.PrefabName),StringComparer.Ordinal);
            var retiredNames=new HashSet<string>(ArmorCatalog.AllPieces.Where(p=>!ArmorCatalog.IsCraftable(p.Style)).Select(p=>p.PrefabName),StringComparer.Ordinal);
            value.pieces=value.pieces.Where(p=>p==null||!ArmorCatalog.IsRetiredHelmetPrefab(p.prefabName)).ToArray();
            value.recipes=value.recipes.Where(r=>r==null||(!retiredNames.Contains(r.prefabName)&&!ArmorCatalog.IsRetiredHelmetPrefab(r.prefabName))).ToArray();
            var presentPieces=new HashSet<string>(value.pieces.Where(p=>p!=null).Select(p=>p.prefabName),StringComparer.Ordinal);
            var presentRecipes=new HashSet<string>(value.recipes.Where(p=>p!=null).Select(p=>p.prefabName),StringComparer.Ordinal);
            value.pieces=value.pieces.Concat(defaults.pieces.Where(p=>addedNames.Contains(p.prefabName)&&!presentPieces.Contains(p.prefabName))).ToArray();
            value.recipes=value.recipes.Concat(defaults.recipes.Where(p=>addedNames.Contains(p.prefabName)&&!presentRecipes.Contains(p.prefabName))).ToArray();
            var names=new HashSet<string>(ArmorCatalog.AllPieces.Select(p=>p.PrefabName),StringComparer.Ordinal);
            if(value.pieces.Length!=names.Count||value.recipes.Length!=defaults.recipes.Length)throw new InvalidDataException("Settings require all registered armor pieces and craftable recipes");
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var p in value.pieces)
            {
                if(p==null||p.prefabName==null||!names.Contains(p.prefabName)||!seen.Add(p.prefabName))throw new InvalidDataException("Unknown or duplicate armor piece");
                if(!Finite(p.damageReduction)||p.damageReduction<0||p.damageReduction>.9f||!Finite(p.maximumDurability)||p.maximumDurability<1||p.maximumDurability>1000000
                    ||!Finite(p.wearPerAbsorbedDamage)||p.wearPerAbsorbedDamage<0||p.wearPerAbsorbedDamage>1000)throw new InvalidDataException("Invalid balance values for "+p.prefabName);
                if((p.projectileDamageReduction.HasValue&&(!Finite(p.projectileDamageReduction.Value)||p.projectileDamageReduction.Value<0||p.projectileDamageReduction.Value>.9f))
                    ||!Finite(p.movementPenalty)||p.movementPenalty<0||p.movementPenalty>.5f)throw new InvalidDataException("Invalid projectile protection or movement penalty for "+p.prefabName);
            }
            seen.Clear();
            foreach(var r in value.recipes)
            {
                if(r==null||r.prefabName==null||!names.Contains(r.prefabName)||!seen.Add(r.prefabName)||r.ingredients==null||r.ingredients.Length<1||r.ingredients.Length>8)throw new InvalidDataException("Invalid armor recipe");
                var codes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var i in r.ingredients)
                    if(i==null||!Code(i.code)||(i.material!=null&&!Code(i.material))||!codes.Add(i.code)||i.quantity<1||i.quantity>100)throw new InvalidDataException("Invalid ingredients for "+r.prefabName);
                if(r.ingredients.Sum(i=>i.quantity)>32)throw new InvalidDataException("Armor recipes allow at most 32 physical pieces: "+r.prefabName);
                ArmorConstructionPlan.Validate(r);
            }
            return value;
        }
        static bool Finite(float v){return !float.IsNaN(v)&&!float.IsInfinity(v);}
        static bool Code(string value)
        {if(string.IsNullOrEmpty(value)||value.Length>64)return false;foreach(char c in value)if(c>127||(!char.IsLetterOrDigit(c)&&c!='_'))return false;return true;}
        internal static ArmorConfiguration Defaults()
        {
            var value=new ArmorConfiguration{schemaVersion=ServerSchemaVersion};var pieces=new List<ArmorPieceSettings>();var recipes=new List<ArmorRecipeSettings>();
            value.woodMaterials=new[]{
                new ArmorWoodMaterialSettings{material="Birch",durabilityMultiplier=.8f,damageReductionMultiplier=.8f},
                new ArmorWoodMaterialSettings{material="Oak"},
                new ArmorWoodMaterialSettings{material="Walnut",durabilityMultiplier=1.2f,damageReductionMultiplier=1.15f},
                new ArmorWoodMaterialSettings{material="Redwood",durabilityMultiplier=1.4f,damageReductionMultiplier=1.3f},
                new ArmorWoodMaterialSettings{material="Ash",durabilityMultiplier=1.6f,damageReductionMultiplier=1.45f}
            };
            foreach(var p in ArmorCatalog.AllPieces)
            {
                bool metal=ArmorCatalog.UsesMetalMaterials(p.Style);
                bool crystal=p.Style==ArmorStyle.CrystalWyrm;
                bool forgeguard=p.Style==ArmorStyle.Forgeguard;
                bool cosmetic=p.Style==ArmorStyle.NewsboyHelmet;
                pieces.Add(new ArmorPieceSettings{prefabName=p.PrefabName,
                    damageReduction=forgeguard?.65f:cosmetic?0f:crystal?CrystalWyrmDamageReduction:metal?.35f:.18f,
                    projectileDamageReduction=forgeguard?(float?).75f:null,movementPenalty=forgeguard?ForgeguardPolicy.Penalty(p.Slot):0,
                    maximumDurability=forgeguard?500:crystal?CrystalWyrmDurability:metal?250:125,wearPerAbsorbedDamage=1});
                if(ArmorCatalog.IsCraftable(p.Style))recipes.Add(DefaultConstruction(p));
            }
            value.pieces=pieces.ToArray();value.recipes=recipes.ToArray();return value;
        }
        static ArmorRecipeSettings DefaultConstruction(ArmorPieceDefinition piece)
        {
            return new ArmorRecipeSettings{prefabName=piece.PrefabName,layoutVersion=3,ingredients=new[]{
                new ArmorIngredientSettings{code=ArmorConstructionPlan.PrimarySource(piece.Style).ToString(System.Globalization.CultureInfo.InvariantCulture),quantity=ArmorConstructionPlan.PrimaryCount(piece.Style,piece.Slot)},
                new ArmorIngredientSettings{code=ArmorConstructionPlan.FastenerSource(piece.Style).ToString(System.Globalization.CultureInfo.InvariantCulture),quantity=ArmorConstructionPlan.FastenerCount(piece.Style,piece.Slot)}
            }};
        }
    }
}
