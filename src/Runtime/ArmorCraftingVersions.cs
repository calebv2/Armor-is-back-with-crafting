using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Alta;
using Alta.Crafting;
using Alta.Networking;
using Alta.Pages;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using Stream=Alta.Serialization.Stream;

namespace PlayerArmorMod
{
    internal static partial class ArmorCrafting
    {
        static readonly Dictionary<uint,Recipe> versions=new Dictionary<uint,Recipe>();
        static readonly List<Recipe> bases=new List<Recipe>();
        static readonly List<Recipe> retiredVersions=new List<Recipe>();
        static readonly HashSet<uint> confirmedVersions=new HashSet<uint>();
        static readonly HashSet<uint> archivedVersionHashes=new HashSet<uint>();
        static string serverScope=Guid.NewGuid().ToString("N"),clientScope;
        static float nextRetiredCollection;
        internal static void SetServerEpoch(string epoch)
        {Guid value;if(!Guid.TryParseExact(epoch,"N",out value))throw new ArgumentException("Armor recipe epoch must be 32 hexadecimal characters");serverScope=value.ToString("N");}
        static readonly MethodInfo nativeRecipeReference=typeof(SerializeHashedObject).GetMethods(BindingFlags.Public|BindingFlags.Static)
            .Single(m=>m.Name=="SerializeHashed"&&m.IsGenericMethodDefinition).MakeGenericMethod(typeof(GenericCraftingRecipe));
        static readonly MethodInfo scopedRecipeReference=AccessTools.Method(typeof(ArmorCrafting),"SerializeRecipeReference");
        static readonly FieldInfo bookPages=ArmorNative.Field(typeof(Book),"bookPages"),insertingPages=ArmorNative.Field(typeof(Book),"inserting");
        static readonly FieldInfo renderedPage=ArmorNative.Field(typeof(PageInstance),"renderedData");

        static void PrepareVersions(List<Recipe> planned,bool complete=true)
        {
            CheckHash<GenericCraftingRecipe>(ArmorCraftingRecipeCodec.Sentinel);
            bases.AddRange(planned);
            if(Core.IsServer)
            {
                string path=HistoryPath();
                if(File.Exists(path))
                {
                    var file=new FileInfo(path);if(file.Length>4*1024*1024)throw new InvalidDataException("recipe-history.json exceeds 4 MiB; preserve the file and restore a valid backup");
                    var history=ArmorCraftingRecipeCodec.ReadHistory(File.ReadAllText(path));
                    // Parse and validate the entire archive before native registration.
                    var seen=new HashSet<uint>();
                    foreach(var item in history.versions)
                    {
                        if(item==null||item.hash==0||item.hash>=ArmorCraftingRecipeCodec.Sentinel||!seen.Add(item.hash))throw new InvalidDataException("Invalid or duplicate archived armor recipe hash");
                        // Reserve every archived identity before selecting any new
                        // active recipe, including expansion entries whose result
                        // prefabs are appended only in the second registration batch.
                        archivedVersionHashes.Add(item.hash);
                        var settings=ArmorCraftingRecipeCodec.Parse(item.descriptor);
                        if(!planned.Any(p=>p.Piece.PrefabName==settings.prefabName))continue;
                        var basis=bases.Single(b=>b.Piece.PrefabName==settings.prefabName);
                        ValidateRecipe(basis,settings);CheckHash<GenericCraftingRecipe>(item.hash);
                    }
                    foreach(var item in history.versions)
                    {var settings=ArmorCraftingRecipeCodec.Parse(item.descriptor);if(planned.Any(p=>p.Piece.PrefabName==settings.prefabName))RegisterVersion(item.hash,item.descriptor);}
                }
            }
            foreach(var basis in planned)SelectActiveVersion(basis,ArmorSettings.ConstructionFor(basis.Piece));
            // First batch must not erase archived expansion recipes before
            // their result prefabs/templates have been appended and loaded.
            if(Core.IsServer&&complete)SaveHistory();
        }
        static void SelectActiveVersion(Recipe basis,ArmorRecipeSettings settings)
        {
            string descriptor=ArmorCraftingRecipeCodec.Canonical(ActiveSettings(settings));
            var version=versions.Values.Where(v=>v.Descriptor==descriptor).OrderByDescending(v=>confirmedVersions.Contains(v.Native.Hash)).FirstOrDefault();
            if(version==null)
            {
                uint hash=ArmorCraftingRecipeCodec.AllocateHash(descriptor,h=>versions.ContainsKey(h)||archivedVersionHashes.Contains(h)||HashedGeneralValue<GenericCraftingRecipe>.All.Any(r=>r.Hash==h));
                version=RegisterVersion(hash,descriptor);
            }
            basis.Native=version.Native;basis.Settings=version.Settings;basis.Descriptor=descriptor;
        }
        // Existing authored configuration gets the new workflow at unchanged
        // ingredient costs. Archived descriptors are parsed verbatim elsewhere,
        // so their levels, nail arrays, identities and project saves stay intact.
        internal static ArmorRecipeSettings ActiveSettings(ArmorRecipeSettings settings)
        {
            if(ArmorConstructionPlan.Layout(settings)!=2)return settings;
            return new ArmorRecipeSettings{prefabName=settings.prefabName,layoutVersion=3,
                ingredients=settings.ingredients.Select(i=>new ArmorIngredientSettings{code=i.code,material=i.material,quantity=i.quantity}).ToArray()};
        }
        static Recipe RegisterVersion(uint hash,string descriptor,bool authoritative=false)
        {
            Recipe existing;
            if(versions.TryGetValue(hash,out existing))
            {
                if(existing.Descriptor==descriptor){if(authoritative)confirmedVersions.Add(hash);return existing;}
                if(authoritative&&!NetworkSceneManager.IsServer&&!confirmedVersions.Contains(hash))Retire(existing);
                else throw new InvalidDataException("Armor recipe hash refers to a different immutable recipe: "+hash);
            }
            if(hash==0||hash>=ArmorCraftingRecipeCodec.Sentinel)throw new InvalidDataException("Invalid armor recipe version hash");
            CheckHash<GenericCraftingRecipe>(hash);
            var settings=ArmorCraftingRecipeCodec.Parse(descriptor);var basis=bases.SingleOrDefault(b=>b.Piece.PrefabName==settings.prefabName);
            if(basis==null)throw new InvalidDataException("Armor recipe arrived before registered armor templates");
            var version=new Recipe{Piece=basis.Piece,Result=basis.Result,Mesh=basis.Mesh,Settings=settings,Descriptor=descriptor};
            var pieces=BuildPieces(version,settings);
            var recipe=ScriptableObject.CreateInstance<GenericCraftingRecipe>();recipe.name="Player Armor Recipe - "+settings.prefabName+" - "+hash;
            ArmorNative.Set(recipe,"hash",(int)hash);ArmorNative.Set(recipe,"resultHash",basis.Result.Hash);ArmorNative.Set(recipe,"pieces",pieces);
            ArmorNative.Set(recipe,"spawnOffset",new SerializedTransform{position=new Vector3(0,.35f,0),rotation=Quaternion.identity,scale=Vector3.one});
            ArmorNative.Set(recipe,"uniqueGlyphs",new Glyph.Positioning[0]);
            bool publish=!ArmorCatalog.IsChestOnly(basis.Piece.Style);
            if(publish)ArmorRecipeBookIntegration.ValidateRegistration(recipe);
            AddValue(recipe);version.Native=recipe;versions.Add(hash,version);
            if(publish)ArmorRecipeBookIntegration.Register(recipe);
            if(authoritative)confirmedVersions.Add(hash);
            ArmorNative.Field(typeof(GenericCraftingRecipe),"mappedResult").SetValue(null,null);return version;
        }
        static void InstallReferenceHooks(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(GenericCraftingRecipePageProvider),"Serialize"),transpiler:new HarmonyMethod(typeof(ArmorCrafting),"ReplaceRecipeReference"));
            harmony.Patch(AccessTools.Method(typeof(GenericCraftingStation),"SyncProject"),transpiler:new HarmonyMethod(typeof(ArmorCrafting),"ReplaceRecipeReference"));
            harmony.Patch(AccessTools.Method(typeof(GenericCraftingRecipe),"GetRecipe"),prefix:new HarmonyMethod(typeof(ArmorCrafting),"GetCurrentRecipe"));
            harmony.Patch(AccessTools.Method(typeof(GenericCraftingRecipe),"MapResults"),prefix:new HarmonyMethod(typeof(ArmorCrafting),"MapRecipeResults"));
            harmony.Patch(AccessTools.Method(typeof(GenericCraftingRecipePageProvider),"GetRandom"),prefix:new HarmonyMethod(typeof(ArmorCrafting),"SelectCurrentRandomRecipe"));
        }
        static IEnumerable<CodeInstruction> ReplaceRecipeReference(IEnumerable<CodeInstruction> instructions)
        {
            int replaced=0;
            foreach(var instruction in instructions)
            {
                var method=instruction.operand as MethodInfo;
                if(instruction.opcode==OpCodes.Call&&method!=null&&method==nativeRecipeReference)
                {instruction.operand=scopedRecipeReference;replaced++;}
                yield return instruction;
            }
            if(replaced!=1)throw new InvalidOperationException("Native armor recipe serialization contract changed: expected exactly one reference call, found "+replaced);
        }
        // Used only by the two native recipe-reference network call sites above.
        // Native world/save serializers retain their original format; the server
        // archive resolves historical recipe hashes before the world is loaded.
        static void SerializeRecipeReference(Stream stream,ref GenericCraftingRecipe value)
        {
            Recipe version=null;bool writing=stream.IsWriting;
            uint hash=writing&&value!=null?value.Hash:0;
            if(writing&&value!=null)versions.TryGetValue(value.Hash,out version);
            string descriptor=version==null?null:version.Descriptor;
            string scope=writing?(NetworkSceneManager.IsServer?serverScope:clientScope):null;
            if(!ArmorCraftingWire.Transfer(new NativeRecipeReferenceStream(stream),ref hash,ref descriptor,ref scope))
            {if(stream.IsReading)value=hash==0?null:HashedGeneralValue<GenericCraftingRecipe>.Get(hash);return;}
            if(stream.IsReading)
            {
                if(NetworkSceneManager.IsServer)
                {
                    if(scope!=serverScope||!versions.TryGetValue(hash,out version)||version.Descriptor!=descriptor)
                        throw new InvalidDataException("Client supplied an unapproved armor recipe descriptor");
                }
                else
                {
                    if(clientScope!=scope)
                    {
                        if(clientScope!=null)RetireCurrentVersions();
                        clientScope=scope;remoteConfigured=false;
                    }
                    version=RegisterVersion(hash,descriptor,true);
                    var basis=bases.Single(b=>b.Piece.PrefabName==version.Piece.PrefabName);
                    if(basis.Descriptor==descriptor||ArmorCraftingRecipeCodec.Canonical(ActiveSettings(ArmorSettings.ConstructionFor(basis.Piece)))==descriptor)
                    {basis.Native=version.Native;basis.Settings=version.Settings;basis.Descriptor=descriptor;}
                }
                value=version.Native;
            }
        }
        sealed class NativeRecipeReferenceStream : IArmorRecipeReferenceStream
        {
            readonly Stream stream;internal NativeRecipeReferenceStream(Stream stream){this.stream=stream;}
            public bool IsWriting {get{return stream.IsWriting;}}
            public void Bits(ref uint value,int count){stream.SerializeBits(ref value,count);}
            public void Integer(ref int value,int minimum,int maximum){stream.SerializeInteger(ref value,minimum,maximum);}
        }
        static bool GetCurrentRecipe(NetworkPrefab prefab,ref GenericCraftingRecipe __result)
        {
            if(prefab!=null&&chestOnlyResults.Contains(prefab.Hash)){__result=null;return false;}
            var current=prefab==null?null:recipes.FirstOrDefault(r=>r.Result.Hash==prefab.Hash);
            if(current==null)return true;__result=current.Native;return false;
        }
        static bool MapRecipeResults()
        {
            var mapped=new Dictionary<uint,GenericCraftingRecipe>();
            foreach(var recipe in HashedGeneralValue<GenericCraftingRecipe>.All)
                if(!versions.ContainsKey(recipe.Hash)&&!IsChestOnlyRecipe(recipe))mapped.Add(recipe.ResultHash,recipe);
            foreach(var current in recipes)mapped.Add(current.Result.Hash,current.Native);
            ArmorNative.Field(typeof(GenericCraftingRecipe),"mappedResult").SetValue(null,mapped);return false;
        }
        static bool SelectCurrentRandomRecipe(GenericCraftingRecipePageProvider __instance,ref PageData __result)
        {
            if(chestOnlyResults.Count==0&&versions.Count<=recipes.Count)return true;
            var pool=(!NetworkSceneManager.Current.IsSimpleServer?HashedGeneralValue<GenericCraftingRecipe>.All:SimpleHashedGeneralValue<GenericCraftingRecipe>.SimpleAll)
                .Where(r=>!IsChestOnlyRecipe(r)&&(!versions.ContainsKey(r.Hash)||recipes.Any(a=>a.Native==r))).ToArray();
            if(pool.Length==0){__result=null;return false;}
            var selected=pool.RandomElement();
            __result=GlobalPagesManager.IsNew(__instance.Identifier,selected.Hash)?__instance.GeneratePage(selected):null;return false;
        }
        internal static void ResetClientSession(string previousScope)
        {
            // An initial ACK/update may run after a native bootstrap. Only the
            // authenticated *old* epoch may clear that epoch's registry.
            if(NetworkSceneManager.IsServer||previousScope==null||clientScope!=previousScope)return;
            RetireCurrentVersions();clientScope=null;remoteConfigured=false;
        }
        static void RetireCurrentVersions()
        {
            foreach(var value in versions.Values.ToArray())Retire(value);
            confirmedVersions.Clear();
            ArmorNative.Field(typeof(HashedGeneralValue<GenericCraftingRecipe>),"itemsByName").SetValue(null,null);
            ArmorNative.Field(typeof(GenericCraftingRecipe),"mappedResult").SetValue(null,null);
            CollectRetired();
        }
        static void Retire(Recipe version)
        {
            ArmorRecipeBookIntegration.Retire(version.Native);
            var map=(Dictionary<uint,GenericCraftingRecipe>)ArmorNative.Field(typeof(HashedGeneralValue<GenericCraftingRecipe>),"items").GetValue(null);
            GenericCraftingRecipe found;
            if(map.TryGetValue(version.Native.Hash,out found)&&object.ReferenceEquals(found,version.Native))map.Remove(version.Native.Hash);
            versions.Remove(version.Native.Hash);confirmedVersions.Remove(version.Native.Hash);
            if(!retiredVersions.Contains(version))retiredVersions.Add(version);
            foreach(var basis in bases)if(object.ReferenceEquals(basis.Native,version.Native))basis.Native=null;
            ArmorNative.Field(typeof(HashedGeneralValue<GenericCraftingRecipe>),"itemsByName").SetValue(null,null);
            ArmorNative.Field(typeof(GenericCraftingRecipe),"mappedResult").SetValue(null,null);
        }
        internal static void Update()
        {
            if(Core.IsServer)
            {
                UpdateWorkshopRefunds();
                return;
            }
            ArmorCraftingJig.Update();
            if(retiredVersions.Count==0||Time.unscaledTime<nextRetiredCollection)return;
            nextRetiredCollection=Time.unscaledTime+2;CollectRetired();
        }
        static void CollectRetired()
        {
            if(retiredVersions.Count==0)return;
            var used=new HashSet<GenericCraftingRecipe>();
            foreach(var station in UnityEngine.Object.FindObjectsOfType<GenericCraftingStation>(true))
            {
                var project=currentProject.GetValue(station) as IProject;if(project!=null&&project.Recipe!=null)used.Add(project.Recipe);
            }
            foreach(var page in UnityEngine.Object.FindObjectsOfType<PageInstance>(true))
            {
                var recipe=page.Data==null?null:page.Data.GetPageComponent<GenericCraftingRecipe>();if(recipe!=null)used.Add(recipe);
                var rendered=renderedPage.GetValue(page) as PageData;recipe=rendered==null?null:rendered.GetPageComponent<GenericCraftingRecipe>();if(recipe!=null)used.Add(recipe);
            }
            // Rendering can be uninitialized or already torn down during a
            // disconnect. An unknown cache is not evidence that recipes are
            // unused: retain them until a later complete reference scan.
            if(!ArmorRecipeRenderReferences.TryCollect(used))return;
            foreach(var book in UnityEngine.Object.FindObjectsOfType<Book>(true))
                foreach(var field in new[]{bookPages,insertingPages})
                {
                    var pages=field.GetValue(book) as IEnumerable<PageData>;if(pages==null)continue;
                    foreach(var page in pages){var recipe=page==null?null:page.GetPageComponent<GenericCraftingRecipe>();if(recipe!=null)used.Add(recipe);}
                }
            for(int i=retiredVersions.Count-1;i>=0;i--)
            {
                var version=retiredVersions[i];if(used.Contains(version.Native))continue;
                foreach(var piece in version.Native.Pieces)
                {materials.Remove(piece);if(piece.collisionMesh!=null&&piece.collisionMesh!=piece.mesh)UnityEngine.Object.Destroy(piece.collisionMesh);if(piece.mesh!=null)UnityEngine.Object.Destroy(piece.mesh);}
                UnityEngine.Object.Destroy(version.Native);retiredVersions.RemoveAt(i);
            }
        }
        static string HistoryPath(){return Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory,"PlayerArmor","recipe-history.json");}
        static void SaveHistory()
        {
            string path=HistoryPath(),temporary=path+".tmp";Directory.CreateDirectory(Path.GetDirectoryName(path));
            var history=new ArmorRecipeHistory{versions=versions.OrderBy(p=>p.Key).Select(p=>new ArmorRecipeHistoryEntry{hash=p.Key,descriptor=p.Value.Descriptor}).ToArray()};
            string json=ArmorCraftingRecipeCodec.WriteHistory(history);
            File.WriteAllText(temporary,json);
            if(File.Exists(path))File.Replace(temporary,path,path+".bak");else File.Move(temporary,path);
        }
    }
}
