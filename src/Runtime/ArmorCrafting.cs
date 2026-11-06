using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Alta;
using Alta.Crafting;
using Alta.Inventory;
using Alta.Networking;
using Alta.Pages;
using HarmonyLib;
using MelonLoader;
using SyncLib.Items;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PlayerArmorMod
{
    // Native recipe-page/GenericCraftingStation integration. This supplies the
    // missing recipe assets; native code still owns project saves, ingredient
    // consumption, nails, cancellation/refunds, completion and item spawning.
    internal static partial class ArmorCrafting
    {
        sealed class Recipe
        {
            internal ArmorPieceDefinition Piece;
            internal NetworkPrefab Result,Page;
            internal GenericCraftingRecipe Native;
            internal Mesh Mesh;
            internal ArmorRecipeSettings Settings;
            internal string Descriptor;
            internal ArmorJigPlan Jig;
        }
        static readonly List<Recipe> recipes=new List<Recipe>();
        static readonly HashSet<uint> chestOnlyResults=new HashSet<uint>();
        static readonly Dictionary<GenericCraftingPieceDefinition,string> materials=new Dictionary<GenericCraftingPieceDefinition,string>();
        static GenericCraftingPieceDefinition[] nativeTemplates;
        static bool installed,registered,remoteConfigured;
        static readonly FieldInfo pieceDefinition=ArmorNative.Field(typeof(GenericCraftingPiece),"definition");
        static readonly FieldInfo pieceProject=ArmorNative.Field(typeof(GenericCraftingPiece),"project");
        static readonly FieldInfo currentProject=ArmorNative.Field(typeof(GenericCraftingStation),"currentProject");

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if(installed)return;installed=true;
            harmony.Patch(AccessTools.Method(typeof(PageInstance),"Generate"),prefix:new HarmonyMethod(typeof(ArmorCrafting),"GeneratePage"));
            harmony.Patch(AccessTools.Method(typeof(GenericCraftingPiece),"AcceptsComponent"),postfix:new HarmonyMethod(typeof(ArmorCrafting),"CheckMaterial"));
            harmony.Patch(AccessTools.Method(typeof(GenericCraftingStation),"TryAddPiece"),prefix:new HarmonyMethod(typeof(ArmorCrafting),"AllowProject"));
            harmony.Patch(AccessTools.Method(typeof(GenericCraftingStation),"TryAddPiece"),prefix:new HarmonyMethod(typeof(ArmorCrafting),"AllowJigTarget"));
            harmony.Patch(AccessTools.Method(typeof(GenericCraftingStation),"GrabResult"),prefix:new HarmonyMethod(typeof(ArmorCrafting),"AllowProject"));
            harmony.Patch(AccessTools.Method(typeof(GenericCraftingStation),"GrabResultFilter"),postfix:new HarmonyMethod(typeof(ArmorCrafting),"FilterProjectGrip"));
            harmony.Patch(AccessTools.Method(typeof(GenericCraftingStation),"SpawnResult"),prefix:new HarmonyMethod(typeof(ArmorCrafting),"AllowProject"));
            harmony.Patch(AccessTools.Method(typeof(Book),"AddNewPage"),prefix:new HarmonyMethod(typeof(ArmorCrafting),"AllowBookPage"));
            harmony.Patch(AccessTools.Method(typeof(GlobalPagesManager),"AddPage",new[]{typeof(int),typeof(uint)}),
                prefix:new HarmonyMethod(typeof(ArmorCrafting),"AllowUnlock"));
            harmony.Patch(AccessTools.Method(typeof(GenericCraftingStation),"TrySetClosestPiece"),transpiler:new HarmonyMethod(typeof(ArmorCrafting),"ExpandArmorSnapRange"));
            harmony.Patch(AccessTools.Method(typeof(GenericCraftingStation),"CheckPageForRecipe"),postfix:new HarmonyMethod(typeof(ArmorCrafting),"CurrentJigFromPage"));
#if SERVER
            InstallDisassembly(harmony);
            InstallWorkshopMigration(harmony);
#endif
            InstallReferenceHooks(harmony);
            InstallMaterialHooks(harmony);
            InstallRecipeLabelHooks(harmony);
            ArmorRecipeBookIntegration.Install(harmony);
            ArmorCraftingDiagnostics.Install(harmony);
#if CLIENT
            ArmorRecipePageInteraction.Install(harmony);
#endif
            ArmorSettings.Changed+=SettingsChanged;
        }
        internal static void Register(MelonMod owner,GameObject staging,ArmorPrefabRegistry assets)
        {RegisterBatch(owner,staging,assets,ArmorCatalog.RecipePagePieces,true);}
        internal static void RegisterBatch(MelonMod owner,GameObject staging,ArmorPrefabRegistry assets,IEnumerable<ArmorPieceDefinition> definitions,bool complete)
        {
            if(registered)return;
#if CLIENT
            ArmorCraftingJig.Configure(assets.VisualTemplate);
#endif
            nativeTemplates=HashedGeneralValue<GenericCraftingRecipe>.All.SelectMany(r=>r.Pieces).Where(p=>p!=null&&p.source!=null&&p.mesh!=null&&p.nailSettings!=null).ToArray();
            if(nativeTemplates.Length==0)throw new InvalidOperationException("Native workbench recipe templates are unavailable");
            // The lower-hash Duplicate Paper Variant (#11048) is deliberately
            // hidden from native spawn lists. Use the original unlockable page.
            var donor=PrefabManager.AllPrefabs.FirstOrDefault(IsOriginalPaperDonor);
            if(donor==null)throw new InvalidOperationException("Native original Paper #37066 is unavailable");
            var planned=new List<Recipe>();
            foreach(var piece in definitions)
            {
                var result=assets.PiecesFor(piece.Style)[(int)piece.Slot];
                if(result==null||result.GetComponent<Pickup>()==null)throw new InvalidOperationException("Missing recipe result "+piece.PrefabName);
                if(ArmorCatalog.IsChestOnly(piece.Style))chestOnlyResults.Add(result.Hash);
                string leaf=piece.ArtRootName.Substring(piece.ArtRootName.LastIndexOf('/')+1);
                var art=assets.VisualTemplate.transform.Find("Pickups/"+piece.Style+"/"+leaf);
                var mesh=art==null?null:art.GetComponent<MeshFilter>();
                if(mesh==null||mesh.sharedMesh==null)throw new InvalidOperationException("Missing compact recipe art "+piece.PrefabName);
                CheckHash<Glyph>(ArmorCraftingPlan.GlyphHash(piece.PrefabName));
                planned.Add(new Recipe{Piece=piece,Result=result,Mesh=mesh.sharedMesh});
            }
            // Compile every configured ingredient before modifying any native
            // recipe dictionary or registering a page prefab.
            foreach(var entry in planned)ValidateRecipe(entry,ArmorSettings.ConstructionFor(entry.Piece));
            for(int i=0;i<planned.Count;i++)
            {
                var entry=planned[i];
                var glyph=ScriptableObject.CreateInstance<Glyph>();glyph.name="Player Armor - "+entry.Piece.PrefabName;
                ArmorNative.Set(glyph,"hash",(int)ArmorCraftingPlan.GlyphHash(entry.Piece.PrefabName));
#if CLIENT
                ArmorNative.Set(glyph,"sprite",RecipeIcon(entry.Piece,entry.Mesh));
#endif
                AddValue(glyph);
                RegisterRecipeLabel(entry.Piece);
                ArmorNative.Set(entry.Result.GetComponent<Pickup>().Item,"glyph",glyph);
            }
            PrepareVersions(planned,complete);
            for(int i=0;i<planned.Count;i++)
            {
                var entry=planned[i];var recipe=entry.Native;
                var template=Object.Instantiate(donor.gameObject,staging.transform,false);template.name=ArmorCraftingPlan.PageName(entry.Piece);
                template.AddComponent<ArmorCraftingPage>().RecipeHash=recipe.Hash;
                entry.Page=owner.RegisterCustomPrefab(template,template.name);
                if(entry.Page==null)throw new InvalidOperationException("Could not register "+template.name);
                if(ArmorCatalog.IsChestOnly(entry.Piece.Style))
                    entry.Page.DisableSpawnThroughCommands(); // reserve its old network ID for restored physical pages
                else
                {
                    EnableRecipePageSpawn(entry.Page);
                    recipes.Add(entry);
                }
                entry.Page.SaveMode=ATT.Saving.SaveMode.Base;BindRecipePage(template);template.SetActive(true);
                Core.Log("PLAYER_ARMOR_RECIPE_REGISTERED name="+template.name+" page="+entry.Page.Hash+" recipe="+recipe.Hash+" pieces="+recipe.Pieces.Length+" spawnable="+entry.Page.CanBeSpawnedThroughCommand);
            }
            ArmorNative.Field(typeof(GenericCraftingRecipe),"mappedResult").SetValue(null,null);
            registered=complete;remoteConfigured=NetworkSceneManager.IsServer||ArmorSettings.RemoteReady;
            if(complete)ArmorRecipeBookIntegration.Refresh();
            Core.Log("PLAYER_ARMOR_CRAFTING_READY enabled="+ArmorSettings.Current.craftingEnabled+" recipes="+recipes.Count+" snapRadius="+IngredientSnapRadius.ToString(CultureInfo.InvariantCulture));
#if SERVER
            string directory=Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory,"PlayerArmor");Directory.CreateDirectory(directory);
            File.WriteAllLines(Path.Combine(directory,"crafting-prefabs.txt"),recipes.Select(r=>ArmorCraftingPlan.PageName(r.Piece)+" #"+r.Page.Hash+" -> "+r.Piece.PrefabName+"; native recipe #"+r.Native.Hash).ToArray());
#endif
        }
        static bool IsOriginalPaperDonor(NetworkPrefab prefab)
        {return prefab!=null&&prefab.Hash==37066u&&prefab.CanBeSpawnedThroughCommand&&prefab.GetComponent<Pickup>()!=null&&prefab.GetComponent<PaperPageInstance>()!=null;}
        static void EnableRecipePageSpawn(NetworkPrefab page)
        {
            if(page==null||page.GetComponent<ArmorCraftingPage>()==null)throw new InvalidOperationException("Spawn visibility can only be enabled on an armor recipe page");
            ArmorNative.Set(page,"canBeSpawnedThroughCommand",true);
            if(!page.CanBeSpawnedThroughCommand)throw new InvalidOperationException("Armor recipe page is still hidden from native spawn lists");
        }
        internal static bool IsRegisteredRecipePage(PageInstance page)
        {
            if(!registered||page==null||page.Entity==null||page.Entity.Prefab==null||page.GetComponent<ArmorCraftingPage>()==null||page.Data==null)return false;
            var native=page.Data.GetPageComponent<GenericCraftingRecipe>();Recipe version;
            if(native==null||!versions.TryGetValue(native.Hash,out version)||!object.ReferenceEquals(version.Native,native))return false;
            return bases.Any(r=>r.Page!=null&&r.Page.Hash==page.Entity.Prefab.Hash&&r.Piece.PrefabName==version.Piece.PrefabName);
        }
        internal static bool IsChestOnlyRecipe(GenericCraftingRecipe recipe)
        {return recipe!=null&&chestOnlyResults.Contains(recipe.ResultHash);}
        static bool AllowBookPage(Book __instance,PageData page)
        {
            var recipe=page?.ProviderData as GenericCraftingRecipe;
            return !IsChestOnlyRecipe(recipe)&&OwnsRecipe(recipe)==ArmorWorkshopPrefabs.IsBook(__instance);
        }
        static bool AllowUnlock(int providerID,uint hash)
        {
            Recipe version;
            return providerID!=2||!versions.TryGetValue(hash,out version)||!ArmorCatalog.IsChestOnly(version.Piece.Style);
        }
        internal static bool OwnsRecipe(GenericCraftingRecipe recipe)
        {Recipe version;return recipe!=null&&versions.TryGetValue(recipe.Hash,out version)&&object.ReferenceEquals(version.Native,recipe);}
        internal static bool OwnsJigRecipe(GenericCraftingRecipe recipe)
        {Recipe version;return recipe!=null&&versions.TryGetValue(recipe.Hash,out version)&&object.ReferenceEquals(version.Native,recipe)&&version.Jig!=null;}
        internal static ArmorJigPlan JigPlanFor(GenericCraftingRecipe recipe)
        {return OwnsJigRecipe(recipe)?versions[recipe.Hash].Jig:null;}
        // Upgrade only new projects selected from an equivalent learned page.
        // Native saved projects call LoadRecipe directly and retain their exact
        // archived recipe/nail arrays. Do not migrate their in-flight state.
        static void CurrentJigFromPage(GenericCraftingStation __instance,ref GenericCraftingRecipe recipe,ref bool __result)
        {
            if(__result&&!AllowsRecipe(__instance,recipe)){recipe=null;__result=false;return;}
            if(!__result||!OwnsRecipe(recipe))return;
            var old=versions[recipe.Hash];
            if(ArmorConstructionPlan.Layout(old.Settings)!=2)return;
            var active=recipes.FirstOrDefault(r=>r.Piece.PrefabName==old.Piece.PrefabName);
            if(active!=null&&OwnsJigRecipe(active.Native)&&active.Descriptor==ArmorCraftingRecipeCodec.Canonical(ActiveSettings(old.Settings)))recipe=active.Native;
        }
        static void BindRecipePage(GameObject template)
        {
            var page=template.GetComponent<PageInstance>();
            var owned=template.GetComponentsInChildren<Collider>(true);
            var configured=ArmorNative.Field(typeof(PageInstance),"colliders").GetValue(page) as Collider[];
            // Preserve the native page's collider selection, excluding stale or
            // null references which its SetInBook loop cannot safely dereference.
            var valid=(configured??new Collider[0]).Where(c=>c!=null&&owned.Contains(c)).Distinct().ToArray();
            if(valid.Length==0)valid=owned.Where(c=>c!=null).Distinct().ToArray();
            if(valid.Length==0)throw new InvalidOperationException("Armor recipe page has no owned colliders");
            ArmorNative.Set(page,"colliders",valid);ArmorNative.Bind(template);
        }
        static GenericCraftingPieceDefinition[] BuildPieces(Recipe recipe,ArmorRecipeSettings settings)
        {
            ValidateRecipe(recipe,settings);
            int count=ArmorCraftingPlan.Count(settings);
            var sources=new List<GenericCraftingPieceDefinition>();var requirements=new List<string>();
            foreach(var ingredient in settings.ingredients)
            {
                var item=FindIngredient(ingredient.code);
                if(item==null||item.Prefab==null||item.Prefab.GetComponent<GenericCraftingComponent>()==null)
                    throw new InvalidOperationException("Armor recipe ingredient "+ingredient.code+" must be a native workbench component, such as metal plates, leather rolls, or rope");
                var source=nativeTemplates.FirstOrDefault(p=>p.source.Prefab.Hash==item.Prefab.Hash);
                if(source==null)throw new InvalidOperationException("No native workbench template for "+ingredient.code);
                if(!string.IsNullOrEmpty(ingredient.material)&&!HasMaterial(ingredient.material))throw new InvalidOperationException("Unknown native physical material "+ingredient.material);
                for(int i=0;i<ingredient.quantity;i++){sources.Add(source);requirements.Add(ingredient.material);}
            }
            var meshes=ArmorConstructionPlan.Layout(settings)>=2?BuildSemanticMeshes(recipe,sources):ArmorCraftingMesh.Partition(recipe.Mesh,count,recipe.Piece.Slot);
            var collisionMeshes=new Mesh[count];
            var result=new GenericCraftingPieceDefinition[count];
            try
            {
            var primary=sources.Select(source=>source.source.Prefab.Hash==ArmorConstructionPlan.PrimarySource(recipe.Piece.Style)).ToArray();
            if(ArmorConstructionPlan.Layout(settings)==3)ArmorCraftingMesh.PrepareJigMeshes(meshes,primary,recipe.Piece.Style,recipe.Piece.Slot);
            for(int i=0;i<count;i++)collisionMeshes[i]=ArmorCraftingMesh.CollisionProxy(meshes[i]);
            var requiresNails=sources.Select(source=>!string.Equals(Compact(source.source.name),"ropeclump",StringComparison.Ordinal)).ToArray();
            Vector3[] nailPositions;Quaternion[] nailRotations;
            if(ArmorConstructionPlan.Layout(settings)==3)
            {
                // Client and server read the same verified stage/nail plan.
                // Do not independently search seams on different platforms.
                recipe.Jig=ArmorCraftingJigPlans.Read(recipe.Piece,sources.Select(source=>source.source.Prefab.Hash).ToArray());
                requiresNails=recipe.Jig.RequiresNails;nailPositions=recipe.Jig.NailPositions;nailRotations=recipe.Jig.NailRotations;
            }
            else ArmorCraftingMesh.NailPoses(meshes,collisionMeshes,out nailPositions,out nailRotations,requiresNails);
            var clearance=ArmorCraftingMesh.LastNailClearanceDegrees;
            Core.Log("PLAYER_ARMOR_NAIL_GEOMETRY name="+recipe.Piece.PrefabName+" layout="+ArmorConstructionPlan.Layout(settings)
                +" nails="+requiresNails.Count(value=>value)+" clear45="+clearance.Count(value=>value==45)
                +" clear20="+clearance.Count(value=>value==20)+" straight="+clearance.Count(value=>value==0)
                +" fallback="+clearance.Count(value=>value==-1));
            if(clearance.Any(value=>value==-1))Core.Error("PLAYER_ARMOR_NAIL_CLEARANCE_LIMITED name="+recipe.Piece.PrefabName+"; this custom/legacy layout has a narrow hammer approach");
            for(int i=0;i<count;i++)
            {
                var original=sources[i];
                bool noNails=!requiresNails[i];
                Vector3 nailPosition=nailPositions[i];Quaternion nailRotation=nailRotations[i];
                var nails=new NailSettings{nails=noNails?new SerializedTransform[0]:new[]{new SerializedTransform{position=nailPosition,rotation=nailRotation,scale=Vector3.one}},
                    nailDefinition=original.nailSettings.nailDefinition??GenericCraftingSettings.GenericNailDefaultDefinition,
                    // A short joint rivet may stay straight; this limits its
                    // physical bend, not which native hammer hits are accepted.
                    nailTime=90,progressHealth=.5f,maxDeviance=recipe.Jig==null?45:Math.Max(0,clearance[i]),maxDamagePerHit=.3f};
                result[i]=new GenericCraftingPieceDefinition{hash=i+1,source=original.source,level=recipe.Jig==null?0:recipe.Jig.Levels[i],mesh=meshes[i],collisionMesh=collisionMeshes[i],
                    position=new SerializedTransform{position=Vector3.zero,rotation=Quaternion.identity,scale=Vector3.one},failureHealth=3,
                    isCritical=false,resultHash=0,minChiselHitDamageToFall=.5f,materialChannel=original.materialChannel,defaultMaterial=original.defaultMaterial,
                    avoidsAtlasing=original.avoidsAtlasing,nailSettings=nails};
                materials.Add(result[i],requirements[i]);
            }
            return result;
            }
            catch
            {
                foreach(var definition in result)if(definition!=null)materials.Remove(definition);
                foreach(var mesh in meshes)if(mesh!=null)Object.DestroyImmediate(mesh);
                foreach(var mesh in collisionMeshes)if(mesh!=null)Object.DestroyImmediate(mesh);
                throw;
            }
        }
        static void ValidateRecipe(Recipe recipe,ArmorRecipeSettings settings)
        {
            ArmorCraftingPlan.Count(settings);
            ArmorConstructionPlan.Validate(settings);
            if(settings.prefabName!=recipe.Piece.PrefabName)throw new InvalidOperationException("Armor recipe result name mismatch");
            var pageSettings=Alta.Global.GlobalSettings<GenericCraftingRecipePageSettings>.Instance;
            if(settings.ingredients.Length>pageSettings.materialPositions.Length)throw new InvalidOperationException("Too many ingredient kinds for native recipe page: "+recipe.Piece.PrefabName);
            foreach(var ingredient in settings.ingredients)
            {
                var item=FindIngredient(ingredient.code);
                if(item==null||item.Prefab==null||item.Prefab.GetComponent<GenericCraftingComponent>()==null||!nativeTemplates.Any(p=>p.source.Prefab.Hash==item.Prefab.Hash))
                    throw new InvalidOperationException("Armor recipe ingredient "+ingredient.code+" must be an existing native workbench component");
                if(item.Glyph==null)throw new InvalidOperationException("Armor recipe ingredient "+ingredient.code+" has no native recipe-page glyph");
                if(!string.IsNullOrEmpty(ingredient.material)&&!HasMaterial(ingredient.material))throw new InvalidOperationException("Unknown native physical material "+ingredient.material);
            }
        }
        static Item FindIngredient(string code)
        {
            uint hash;if(uint.TryParse(code,NumberStyles.None,CultureInfo.InvariantCulture,out hash))
                return HashedGeneralValue<Item>.All.FirstOrDefault(i=>i.Prefab!=null&&i.Prefab.Hash==hash);
            string key=Compact(code);return HashedGeneralValue<Item>.All.FirstOrDefault(i=>Compact(i.name)==key);
        }
        static string Compact(string text){return text.Replace(" ","").Replace("_","").ToLowerInvariant();}
        static bool HasMaterial(string name){return HashedGeneralValue<PhysicalMaterial>.All.Any(m=>ArmorCraftingPlan.MaterialMatches(name,m.name));}
        static void CheckHash<T>(uint hash) where T:HashedGeneralValue<T>
        {if(HashedGeneralValue<T>.All.Any(value=>value.Hash==hash))throw new InvalidOperationException("Player Armor native "+typeof(T).Name+" hash collision: "+hash);}
        static void AddValue<T>(T value) where T:HashedGeneralValue<T>
        {
            var map=(Dictionary<uint,T>)ArmorNative.Field(typeof(HashedGeneralValue<T>),"items").GetValue(null);map.Add(value.Hash,value);
            ArmorNative.Field(typeof(HashedGeneralValue<T>),"itemsByName").SetValue(null,null);
        }
        static void SettingsChanged()
        {
            if(registered)Core.Log("PLAYER_ARMOR_CRAFTING_SETTINGS enabled="+ArmorSettings.Current.craftingEnabled);
            // Native projects save per-piece state, so their recipe shape is
            // immutable for a server session. A client accepts the server's
            // initial recipe configuration once, before the armor session ACK.
            if(!NetworkSceneManager.IsServer&&!ArmorSettings.RemoteReady){remoteConfigured=false;return;}
            if(!registered||remoteConfigured||NetworkSceneManager.IsServer)return;
            try
            {
                foreach(var recipe in recipes)SelectActiveVersion(recipe,ArmorSettings.GetRecipe(recipe.Piece.PrefabName));
                remoteConfigured=true;Core.Log("PLAYER_ARMOR_RECIPES_CONFIGURED server recipe settings accepted");
            }
            catch(Exception e){Core.Error("PLAYER_ARMOR_RECIPES_CONFIG_FAILED "+e.GetBaseException().Message);}
        }
        static bool GeneratePage(PageInstance __instance)
        {
            var marker=__instance.GetComponent<ArmorCraftingPage>();if(marker==null)return true;
            if(__instance.Data!=null)return false; // a loaded historical page keeps its saved recipe
            Recipe entry;versions.TryGetValue(marker.RecipeHash,out entry);
            if(entry==null)throw new InvalidOperationException("Armor recipe page is not registered: "+marker.RecipeHash);
            __instance.Set(entry.Native.CreatePage(2));return false;
        }
        static void CheckMaterial(GenericCraftingPiece __instance,GenericCraftingComponent component,ref bool __result)
        {
            if(!__result)return;
            var definition=pieceDefinition.GetValue(__instance) as GenericCraftingPieceDefinition;string required;
            if(definition==null||!materials.TryGetValue(definition,out required))return;
            var project=pieceProject.GetValue(__instance) as IProject;
            if(project!=null&&!AllowsRecipe(project.Station,project.Recipe)){__result=false;return;}
            // Native SetMaterial(null) leaves a piece empty while the ingredient
            // is consumed. Require a real material even for unfiltered sources.
            __result=ArmorSettings.Current.craftingEnabled&&component!=null&&component.Material!=null
                &&ArmorCraftingPlan.MaterialMatches(required,component.Material.name);
        }
        static bool AllowProject(GenericCraftingStation __instance)
        {
            var project=currentProject.GetValue(__instance) as IProject;
            if(project==null||project.Recipe==null)return true;
            return AllowsRecipe(__instance,project.Recipe)&&(ArmorSettings.Current.craftingEnabled||!OwnsRecipe(project.Recipe));
        }
        static bool AllowsRecipe(GenericCraftingStation station,GenericCraftingRecipe recipe)
        {return !IsChestOnlyRecipe(recipe)&&OwnsRecipe(recipe)==ArmorWorkshopPrefabs.IsBench(station);}
        static void FilterProjectGrip(GenericCraftingStation __instance,ref bool result)
        {if(result&&!AllowProject(__instance))result=false;}
        static bool AllowJigTarget(GenericCraftingStation __instance,int bestPieceKey)
        {
            var project=currentProject.GetValue(__instance) as IProject;
            if(project==null||!OwnsJigRecipe(project.Recipe))return true;
            GenericCraftingPiece piece;
            return bestPieceKey>0&&project.MappedPieces.TryGetValue(bestPieceKey,out piece)&&piece!=null&&piece.IsEnabled&&!piece.IsFilled;
        }
        internal static void Dispose(){ArmorSettings.Changed-=SettingsChanged;ArmorCraftingDiagnostics.Dispose();ArmorRecipeBookIntegration.Dispose();
            ArmorCraftingJigPlans.Dispose();
#if CLIENT
            ArmorCraftingJig.Dispose();
            ArmorRecipeGlyphArt.Dispose();
#endif
        }
    }
    internal sealed class ArmorCraftingPage : MonoBehaviour
    {public uint RecipeHash;}
}
