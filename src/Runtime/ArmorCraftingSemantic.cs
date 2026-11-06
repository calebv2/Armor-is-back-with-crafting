using System;
using System.Collections.Generic;
using Alta.Crafting;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static partial class ArmorCrafting
    {
        static Mesh[] BuildSemanticMeshes(Recipe recipe,List<GenericCraftingPieceDefinition> sources)
        {
            int primaryCount=ArmorConstructionPlan.PrimaryCount(recipe.Piece.Style,recipe.Piece.Slot);
            int fastenerCount=ArmorConstructionPlan.FastenerCount(recipe.Piece.Style,recipe.Piece.Slot);
            if(ArmorSemanticLayout.Count(recipe.Mesh,ArmorSemanticLayout.Primary)!=primaryCount)
                throw new InvalidOperationException("Armor art construction layout differs from its recipe: "+recipe.Piece.PrefabName);
            int authoredFasteners=ArmorSemanticLayout.Count(recipe.Mesh,ArmorSemanticLayout.Fastener);
            if(authoredFasteners!=fastenerCount)throw new InvalidOperationException("Armor art fastening layout differs from its recipe");
            uint primaryHash=ArmorConstructionPlan.PrimarySource(recipe.Piece.Style);
            uint fastenerHash=ArmorConstructionPlan.FastenerSource(recipe.Piece.Style);
            var vertices=ArmorCraftingMesh.Assembly(recipe.Mesh,recipe.Piece.Slot);
            var meshes=new Mesh[sources.Count];int primary=0,fastener=0;
            try
            {
            for(int i=0;i<sources.Count;i++)
            {
                uint hash=sources[i].source.Prefab.Hash;
                if(hash==primaryHash)
                {
                    meshes[i]=ArmorSemanticLayout.Extract(recipe.Mesh,ArmorSemanticLayout.Primary,primary++,vertices);
                    if(meshes[i]==null)throw new InvalidOperationException("Missing complete armor panel");
                }
                else if(hash==fastenerHash)
                {
                    int group=fastener++;
                    meshes[i]=ArmorSemanticLayout.Extract(recipe.Mesh,ArmorSemanticLayout.Fastener,group,vertices);
                    if(meshes[i]==null)throw new InvalidOperationException("Missing complete armor fastening region");
                }
                else throw new InvalidOperationException("Unsupported component in authored armor construction");
            }
            if(primary!=primaryCount||fastener!=fastenerCount)throw new InvalidOperationException("Incomplete armor construction recipe");
            return meshes;
            }
            catch
            {
                foreach(var mesh in meshes)if(mesh!=null)UnityEngine.Object.DestroyImmediate(mesh);
                throw;
            }
        }
    }
}
