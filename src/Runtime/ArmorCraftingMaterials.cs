using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Alta.Chunks;
using Alta.Crafting;
using Alta.Inventory;
using Alta.Networking;
using HarmonyLib;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static partial class ArmorCrafting
    {
        const uint PrimaryPlatePrefab=50988;
        const uint MetalFastenerPrefab=62594;
        sealed class CraftedMaterialProfile
        {
            internal uint ResultHash;
            internal ArmorStyle Style;
            internal ArmorSlot Slot;
            internal float Multiplier;
            internal uint[] Materials;
            internal uint LayoutVersion;
        }
        static readonly MethodInfo nativeMaterialSpawn=typeof(SpawnHelper).GetMethod("Spawn",new[]{typeof(uint),typeof(SpawnData),typeof(Chunk),typeof(Vector3),typeof(Quaternion),typeof(SpawnHelper.SpawningCallback)});
        static readonly MethodInfo materialSpawn=AccessTools.Method(typeof(ArmorCrafting),"SpawnWithArmorMaterials");

        static void InstallMaterialHooks(HarmonyLib.Harmony harmony)
        {
            if(nativeMaterialSpawn==null)throw new InvalidOperationException("Native armor result spawn signature changed");
            harmony.Patch(AccessTools.Method(typeof(GenericCraftingStation),"GrabResult"),prefix:new HarmonyMethod(typeof(ArmorCrafting),"ValidateArmorMaterialResult"));
            harmony.Patch(AccessTools.Method(typeof(GenericCraftingStation),"SpawnResult"),transpiler:new HarmonyMethod(typeof(ArmorCrafting),"TransferArmorMaterials"));
        }
        static bool ValidateArmorMaterialResult(GenericCraftingStation __instance)
        {
            if(!NetworkSceneManager.IsServer)return true;
            var project=currentProject.GetValue(__instance) as IProject;
            if(project==null||!project.IsComplete||!OwnsRecipe(project.Recipe))return true;
            try{CaptureCraftOrigin(__instance,project.Recipe.ResultHash);CaptureArmorMaterials(__instance,project.Recipe.ResultHash);return true;}
            catch(Exception error)
            {
                // Reject before native GrabResult disables its interaction
                // collider or SpawnResult clears/restarts the completed project.
                Core.Error("PLAYER_ARMOR_CRAFT_MATERIAL_REJECTED recipe="+project.Recipe.Hash+" reason="+error.GetBaseException().Message);
                return false;
            }
        }
        static CraftedMaterialProfile CaptureArmorMaterials(GenericCraftingStation station,uint resultHash)
        {
            var project=station==null?null:currentProject.GetValue(station) as IProject;
            if(project==null||!OwnsRecipe(project.Recipe))return null;
            Recipe version=versions[project.Recipe.Hash];
            var style=version.Piece.Style;
            if(!ArmorCatalog.UsesCraftMaterials(style))return null;
            if(!project.IsComplete)throw new InvalidOperationException("Armor material profile requires a completed native project");
            if(version.Result==null||version.Result.Hash!=resultHash||project.Recipe.ResultHash!=resultHash)
                throw new InvalidOperationException("Armor recipe result identity does not match its registered prefab");
            var template=version.Result.GetComponent<ArmorPickup>();
            if(template==null||template.Style!=version.Piece.Style||template.Slot!=(int)version.Piece.Slot)
                throw new InvalidOperationException("Registered armor result marker does not match its recipe");
            if(version.Result.GetComponent<ArmorMaterialState>()==null)
                throw new InvalidOperationException("Registered armor result has no material profile save component");
            uint layout=ArmorConstructionPlan.MaterialLayout(version.Settings);
            uint primarySource=ArmorConstructionPlan.PrimarySource(style);
            uint closureSource=ArmorConstructionPlan.FastenerSource(style);
            bool tintFasteners=ArmorCatalog.TintsFasteners(style);
            var hashes=new List<uint>();var fasteners=new List<uint>();double total=0;
            foreach(var definition in project.Recipe.Pieces)
            {
                if(definition==null||definition.source==null||definition.source.Prefab==null)
                {
                    if(layout==2)throw new InvalidOperationException("Authored armor has a missing native component source");
                    continue;
                }
                uint source=definition.source.Prefab.Hash;
                if(layout==2&&source!=primarySource&&source!=closureSource)
                    throw new InvalidOperationException("Authored armor has an unsupported native component source");
                bool primary=source==primarySource||(layout==1&&style==ArmorStyle.Leather&&source==23206u);
                if(!primary&&(layout!=2||source!=closureSource))continue;
                var material=CaptureFilledArmorMaterial(project,definition,primary?"primary panel":"fastener");
                if(primary)
                {
                    float durability=ArmorCatalog.UsesMetalMaterials(style)?material.DurabilityMultiplier:
                        style==ArmorStyle.Woodland?ArmorWoodPolicy.DurabilityMultiplier(material):1f;
                    if(!FiniteMaterialMultiplier(durability))throw new InvalidOperationException("A primary panel has invalid material durability");
                    hashes.Add(material.Hash);total+=durability;
                }
                else fasteners.Add(material.Hash);
            }
            // A custom historical leather or Woodland recipe can use entirely different ingredients.
            if(layout==1&&hashes.Count==0&&!ArmorCatalog.UsesMetalMaterials(style))return null;
            if(hashes.Count==0||hashes.Count>ArmorMaterialPolicy.MaximumPrimaryMaterials)throw new InvalidOperationException("Armor recipe has an invalid primary panel count");
            float multiplier=(float)(total/hashes.Count);
            if(!ArmorMaterialPolicy.IsValidMultiplier(multiplier))throw new InvalidOperationException("Primary panel durability average is invalid or outside the saved profile bounds");
            if(layout==2)
            {
                if(hashes.Count!=ArmorConstructionPlan.PrimaryCount(style,version.Piece.Slot)
                    ||fasteners.Count!=ArmorConstructionPlan.FastenerCount(style,version.Piece.Slot))
                    throw new InvalidOperationException("Authored armor has an incomplete primary/fastener material palette");
                // Existing layout2 profiles contain primary colors only. New
                // profiles append actual closure colors after every primary;
                // fasteners never contribute to the durability average above.
                if(tintFasteners)hashes.AddRange(fasteners);
                if(hashes.Count>ArmorMaterialPolicy.MaximumPrimaryMaterials)throw new InvalidOperationException("Armor material palette exceeds its fixed saved capacity");
            }
            return new CraftedMaterialProfile{ResultHash=resultHash,Style=version.Piece.Style,Slot=version.Piece.Slot,Multiplier=multiplier,Materials=hashes.ToArray(),LayoutVersion=layout};
        }
        static PhysicalMaterial CaptureFilledArmorMaterial(IProject project,GenericCraftingPieceDefinition definition,string role)
        {
            GenericCraftingPiece piece;
            if(!project.MappedPieces.TryGetValue(definition.hash,out piece)||piece==null||!ReferenceEquals(pieceDefinition.GetValue(piece),definition)||!piece.IsFilled)
                throw new InvalidOperationException("A required "+role+" is absent or unfilled");
            var material=piece.Material;
            if(material==null||material.Hash==0)throw new InvalidOperationException("A "+role+" has missing or invalid native material");
            return material;
        }
        static bool FiniteMaterialMultiplier(float value){return !float.IsNaN(value)&&!float.IsInfinity(value)&&value>0;}
        static NetworkEntity SpawnWithArmorMaterials(uint prefabHash,SpawnData spawnData,Chunk chunk,Vector3 position,Quaternion rotation,SpawnHelper.SpawningCallback preChunking,GenericCraftingStation station)
        {
            // Snapshot before native SpawnResult clears the assembled pieces;
            // it reads their saved materials, never destroyed input pickups.
            var origin=CaptureCraftOrigin(station,prefabHash);
            if(origin==null)return SpawnHelper.Spawn(prefabHash,spawnData,chunk,position,rotation,preChunking);
            var profile=CaptureArmorMaterials(station,prefabHash);
            return SpawnHelper.Spawn(prefabHash,spawnData,chunk,position,rotation,entity=>
            {
                try
                {
                    preChunking?.Invoke(entity);
                    var marker=entity==null?null:entity.GetComponent<ArmorPickup>();
                    if(marker==null||entity.Prefab==null||entity.Prefab.Hash!=prefabHash||marker.Style!=origin.Style||marker.Slot!=origin.Slot)
                        throw new InvalidOperationException("Spawned armor differs from its completed recipe");
                    var record=entity.GetComponent<ArmorCraftRecord>();
                    if(record==null)throw new InvalidOperationException("Armor result is missing its craft record");
                    record.Store(origin.Recipe,origin.Materials);
                    if(profile!=null)ArmorMaterialState.ApplyCrafted(marker,profile.Multiplier,profile.Materials,profile.LayoutVersion);
                }
                catch
                {
                    if(entity!=null && !entity.IsBeingDestroyed)entity.SceneDestroy();
                    throw;
                }
            });
        }
        static IEnumerable<CodeInstruction> TransferArmorMaterials(IEnumerable<CodeInstruction> instructions)
        {
            int replaced=0;
            foreach(var instruction in instructions)
            {
                if(instruction.Calls(nativeMaterialSpawn))
                {
                    var station=new CodeInstruction(OpCodes.Ldarg_0);
                    station.labels.AddRange(instruction.labels);instruction.labels.Clear();
                    station.blocks.AddRange(instruction.blocks);instruction.blocks.Clear();
                    yield return station;
                    instruction.opcode=OpCodes.Call;instruction.operand=materialSpawn;replaced++;
                }
                yield return instruction;
            }
            if(replaced!=1)throw new InvalidOperationException("Native armor result material contract changed: expected one spawn call, found "+replaced);
        }
    }
}
