using System;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Alta;
using Alta.Crafting;
using Alta.Inventory;
using Alta.Networking;
using HarmonyLib;

namespace PlayerArmorMod
{
    // ATT discovers this interface on the pickup rigidbody when it enters the bench.
    // Its normal hand-release coroutine checks overlap, occupied benches and re-grabs.
    internal sealed partial class ArmorPickup : IGenericCraftingComponent
    {
        bool openingForDisassembly;
        bool IGenericCraftingComponent.IsAddingProject { get { return Core.IsServer; } }
        void IGenericCraftingComponent.Track(GenericCraftingStation station) {}
        void IGenericCraftingComponent.Released(GenericCraftingStation station)
        {
            if(!NetworkSceneManager.IsServer || openingForDisassembly)return;
            openingForDisassembly=true;
            try{ArmorCrafting.Disassemble(this,station);}
            catch(Exception error){Core.Error("PLAYER_ARMOR_DISASSEMBLY_REJECTED "+error.GetBaseException().Message);}
            finally{openingForDisassembly=false;}
        }
    }

    internal static partial class ArmorCrafting
    {
        static class DisassemblyStation
        {
            internal static readonly FieldInfo Tracked=ArmorNative.Field(typeof(GenericCraftingStation),"trackedPickups");
            internal static readonly FieldInfo AllowSync=ArmorNative.Field(typeof(GenericCraftingStation),"isSyncingAllowed");
            internal static readonly FieldInfo FullSync=ArmorNative.Field(typeof(GenericCraftingStation),"syncFull");
            internal static readonly FieldInfo PageDock=ArmorNative.Field(typeof(GenericCraftingStation),"recipeDock");
            internal static readonly MethodInfo ClearLOD=AccessTools.Method(typeof(GenericCraftingStation),"ClearLOD");
            internal static readonly MethodInfo SetupLOD=AccessTools.Method(typeof(GenericCraftingStation),"SetupLOD");
        }

        internal static void InstallDisassembly(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(GenericCraftingPiece),"Pop"),
                prefix:new HarmonyMethod(typeof(ArmorCrafting),"RememberDisassemblyProject"),
                postfix:new HarmonyMethod(typeof(ArmorCrafting),"FinishEmptyDisassembly"));
        }

        static void RememberDisassemblyProject(GenericCraftingPiece __instance,out IProject __state)
        {
            var project=(IProject)pieceProject.GetValue(__instance);
            __state=project!=null && OwnsRecipe(project.Recipe)?project:null;
        }
        static void FinishEmptyDisassembly(IProject __state)
        {
            if(__state==null)return;
            var station=__state.Station;
            if(station==null || !ReferenceEquals(currentProject.GetValue(station),__state))return;
            var dock=(PickupDock)DisassemblyStation.PageDock.GetValue(station);
            // Native ReevaluateLevel skips ChangedPieces when a sibling at the
            // same level was removed. A page-free project with no parts must close.
            if(dock!=null && dock.QuantityStored==0 && __state.MappedPieces.Values.All(p=>!p.IsFilled))
                station.ChangedPieces();
        }

        static bool EligibleForDisassembly(ArmorPickup marker,GenericCraftingStation station)
        {
            if(!NetworkSceneManager.IsServer || !ArmorSettings.Current.craftingEnabled || !ArmorWorkshopPrefabs.IsBench(station) ||
                marker==null || station==null || marker.Entity==null || marker.Entity.IsBeingDestroyed ||
                station.Entity==null || station.Entity.IsBeingDestroyed || station.Entity.Chunk==null ||
                currentProject.GetValue(station)!=null || !(bool)DisassemblyStation.AllowSync.GetValue(station))return false;
            var pickup=marker.Entity.CommonPickup;
            var dock=(PickupDock)DisassemblyStation.PageDock.GetValue(station);
            var tracked=(IDictionary)DisassemblyStation.Tracked.GetValue(station);
            return pickup!=null && pickup.Rigidbody!=null && !pickup.IsDocked && !pickup.IsCrafted &&
                !pickup.IsInteractedWith && dock!=null && dock.QuantityStored==0 && tracked.Contains(pickup.Rigidbody);
        }

        static bool MatchesArmor(Recipe version,ArmorPickup marker)
        {
            return version!=null && version.Result!=null && marker.Entity.Prefab!=null &&
                version.Result.Hash==marker.Entity.Prefab.Hash && version.Native.ResultHash==version.Result.Hash &&
                version.Piece.Style==marker.Style && (int)version.Piece.Slot==marker.Slot;
        }

        internal static void Disassemble(ArmorPickup marker,GenericCraftingStation station)
        {
            if(!EligibleForDisassembly(marker,station))return;
            uint[] hashes;Recipe version;
            uint historical;
            if(TryGetCraftRecord(marker,out historical,out hashes))
            {
                if(!versions.TryGetValue(historical,out version) || !MatchesArmor(version,marker))
                    throw new InvalidOperationException("The saved armor recipe is unavailable or belongs to another item.");
            }
            else
            {
                if(HasCraftRecord(marker))throw new InvalidOperationException("Invalid armor craft record.");
                ResolveLegacyDisassembly(marker,out version,out hashes);
            }
            var pieces=version.Native.Pieces;
            if(pieces==null || pieces.Length==0 || pieces.Length>ArmorCraftingPlan.MaximumPieces || hashes.Length!=pieces.Length)
                throw new InvalidOperationException("Ingredient record differs from its recipe.");
            var physical=new PhysicalMaterial[pieces.Length];
            for(int i=0;i<pieces.Length;i++)
            {
                var def=pieces[i];
                if(def==null || def.source==null || def.source.Prefab==null || def.source.Prefab.GetComponent<GenericCraftingComponent>()==null)
                    throw new InvalidOperationException("A native ingredient prefab is unavailable.");
                physical[i]=hashes[i]==0?null:HashedGeneralValue<PhysicalMaterial>.Get(hashes[i]);
                string required;
                if((hashes[i]!=0 && physical[i]==null) || !materials.TryGetValue(def,out required) ||
                    !ArmorCraftingPlan.MaterialMatches(required,physical[i]==null?null:physical[i].name))
                    throw new InvalidOperationException("Unresolved or invalid material for ingredient "+def.hash+".");
            }
            RestoreForDisassembly(marker,station,version.Native,physical);
        }

        // Old or admin-spawned items predate exact source records. Use the
        // active server recipe. A compatible saved plate palette wins over its defaults.
        static void ResolveLegacyDisassembly(ArmorPickup marker,out Recipe version,out uint[] hashes)
        {
            version=bases.FirstOrDefault(candidate=>MatchesArmor(candidate,marker));
            if(version==null)throw new InvalidOperationException("No active recipe for this older armor.");
            var definitions=version.Native.Pieces;
            if(definitions==null || definitions.Length==0 || definitions.Length>ArmorCraftingPlan.MaximumPieces)
                throw new InvalidOperationException("Invalid active recipe for this older armor.");

            var state=marker.GetComponent<ArmorMaterialState>();
            uint[] palette=null;uint layout=0;
            if(state!=null && !state.ReadOldCraftPalette(out palette,out layout))
                throw new InvalidOperationException("Invalid legacy armor material profile.");
            uint closureId=ArmorConstructionPlan.FastenerSource(version.Piece.Style);
            int primaryCount=definitions.Count(d=>d.source.Prefab.Hash==PrimaryPlatePrefab);
            int closureCount=definitions.Count(d=>d.source.Prefab.Hash==closureId);
            bool usePalette=palette!=null && ArmorConstructionPlan.MaterialLayout(version.Settings)==layout &&
                primaryCount>0 && (palette.Length==primaryCount || palette.Length==primaryCount+closureCount);
            int primary=0,closure=primaryCount;
            hashes=new uint[definitions.Length];
            for(int i=0;i<definitions.Length;i++)
            {
                var def=definitions[i];
                uint source=def.source.Prefab.Hash;uint saved=0;
                if(usePalette && source==PrimaryPlatePrefab)saved=palette[primary++];
                else if(usePalette && source==closureId && closure<palette.Length)saved=palette[closure++];
                string required=materials[def];
                if(saved!=0)
                {
                    var metal=HashedGeneralValue<PhysicalMaterial>.Get(saved);
                    if(metal==null)throw new InvalidOperationException("Unknown saved legacy armor material "+saved+".");
                    if(ArmorCraftingPlan.MaterialMatches(required,metal.name)){hashes[i]=saved;continue;}
                }
                var fallback=DefaultDisassemblyMaterial(def,required);
                hashes[i]=fallback==null?0u:fallback.Hash;
            }
        }

        static PhysicalMaterial DefaultDisassemblyMaterial(GenericCraftingPieceDefinition def,string required)
        {
            uint source=def.source.Prefab.Hash;
            string preferred=!string.IsNullOrEmpty(required)?required:
                source==PrimaryPlatePrefab || source==MetalFastenerPrefab?"Iron":
                source==47760u || source==23206u || source==63204u?"Leather":null;
            if(preferred!=null)
            {
                var common=HashedGeneralValue<PhysicalMaterial>.All.FirstOrDefault(m=>m!=null &&
                    string.Equals(m.name,preferred,StringComparison.OrdinalIgnoreCase));
                if(common!=null && ArmorCraftingPlan.MaterialMatches(required,common.name))return common;
            }
            var native=def.source.Prefab.GetComponent<GenericCraftingComponent>().Material;
            if(ArmorCraftingPlan.MaterialMatches(required,native==null?null:native.name))return native;
            var match=HashedGeneralValue<PhysicalMaterial>.All.Where(m=>m!=null && ArmorCraftingPlan.MaterialMatches(required,m.name))
                .OrderBy(m=>m.Hash).FirstOrDefault();
            if(match==null)throw new InvalidOperationException("No available material for legacy ingredient "+source+".");
            return match;
        }

        static void RestoreForDisassembly(ArmorPickup marker,GenericCraftingStation station,GenericCraftingRecipe recipe,PhysicalMaterial[] physical)
        {
            var entity=marker.Entity;
            var chunk=station.Entity.Chunk;
            var sync=(MethodSync)DisassemblyStation.FullSync.GetValue(station);
            if(sync==null || !EligibleForDisassembly(marker,station))return;
            IProject project=null;NetworkEntity loose=null;
            bool consumed=false;
            DisassemblyStation.AllowSync.SetValue(station,false);
            try
            {
                project=recipe.InitializeProject(station);
                currentProject.SetValue(station,project);
                int outer=0;
                for(int i=0;i<recipe.Pieces.Length;i++)
                {
                    var def=recipe.Pieces[i];
                    var piece=project.MappedPieces[def.hash];
                    piece.ApplySaveData(new PieceSaveData{hash=def.hash,isComplete=true,material=physical[i]},chunk);
                    piece.SetEnabled(true);
                    if(def.level>=recipe.Pieces[outer].level)outer=i;
                }
                project.SetMaxLevel();
                // Open the final joint now. Thus the native completed-item grab
                // never offers a free, freshly repaired copy of the input.
                var last=recipe.Pieces[outer];
                var detached=project.MappedPieces[last.hash];
                // Capture the allocated loose entity before initialization so a failed
                // native setup can roll back both the partial spawn and station.
                SpawnHelper.Spawn(last.source.Prefab,SpawnData.DontGenerate,chunk,
                    spawned=>{loose=spawned;spawned.transform.SetPositionAndRotation(detached.CenterPosition,detached.Rotation);},
                    spawned=>spawned.GetComponent<GenericCraftingComponent>().SetupAsRefuse(detached.Material));
                if(loose==null || loose.IsBeingDestroyed)throw new InvalidOperationException("Could not detach the outer ingredient.");
                detached.Empty();
                if(recipe.Pieces.Length==1)
                {
                    project.Clear(false);
                    currentProject.SetValue(station,null);
                    project=null;
                }
                // The missing piece is at the highest level. The native level scan therefore
                // stops at the maximum level even if only the root is still filled.
                else DisassemblyStation.SetupLOD.Invoke(station,null);

                entity.SceneDestroy();
                consumed=entity==null || entity.IsBeingDestroyed;
                if(!consumed)throw new InvalidOperationException("Input armor was not consumed.");
            }
            finally
            {
                // Native SceneDestroy sets this synchronously. If a destroy
                // callback failed after commitment, retain the recovered parts.
                consumed=consumed || entity==null || entity.IsBeingDestroyed;
                try
                {
                    if(!consumed)
                    {
                        try{if(loose!=null && !loose.IsBeingDestroyed)loose.SceneDestroy();}
                        finally
                        {
                            if(project!=null)
                            {
                                try{project.Clear(false);}
                                finally{currentProject.SetValue(station,null);DisassemblyStation.ClearLOD.Invoke(station,null);}
                            }
                        }
                    }
                }
                finally{DisassemblyStation.AllowSync.SetValue(station,true);}
                if(consumed)sync.SendToChunks();
            }
        }
    }
}
