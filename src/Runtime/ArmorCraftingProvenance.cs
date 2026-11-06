using System;
using System.Collections.Generic;
using Alta.Crafting;
using Alta.Networking;

namespace PlayerArmorMod
{
    internal static partial class ArmorCrafting
    {
        internal static bool HasCraftRecord(ArmorPickup armor)
        {
            var state=armor==null?null:armor.GetComponent<ArmorCraftRecord>();
            return state!=null && state.HasRecord;
        }

        internal static bool TryGetCraftRecord(ArmorPickup armor,out uint hash,out uint[] materials)
        {
            hash=0; materials=null;
            var state=armor==null?null:armor.GetComponent<ArmorCraftRecord>();
            return state!=null && state.TryRead(out hash,out materials);
        }

        sealed class CraftOrigin
        {
            internal uint Recipe;
            internal ArmorStyle Style;
            internal int Slot;
            internal uint[] Materials;
        }

        static CraftOrigin CaptureCraftOrigin(GenericCraftingStation station,uint resultHash)
        {
            var project=station==null?null:currentProject.GetValue(station) as IProject;
            if(project==null || !OwnsRecipe(project.Recipe))return null;
            var recipe=project.Recipe;
            var version=versions[recipe.Hash];
            if(!project.IsComplete || recipe.Hash==0 || recipe.Hash>=ushort.MaxValue || version.Result==null ||
                version.Result.Hash!=resultHash || recipe.ResultHash!=resultHash || version.Result.GetComponent<ArmorCraftRecord>()==null)
                throw new InvalidOperationException("Cannot record the completed armor recipe");
            var marker=version.Result.GetComponent<ArmorPickup>();
            if(marker==null || marker.Style!=version.Piece.Style || marker.Slot!=(int)version.Piece.Slot)
                throw new InvalidOperationException("Armor craft origin result marker mismatch");
            var definitions=recipe.Pieces;
            if(definitions==null || definitions.Length==0 || definitions.Length>ArmorCraftRecord.MaxPieces || project.MappedPieces.Count!=definitions.Length)
                throw new InvalidOperationException("Incomplete armor craft origin");
            var hashes=new uint[definitions.Length];
            var keys=new HashSet<int>();
            for(int i=0;i<definitions.Length;i++)
            {
                var def=definitions[i];GenericCraftingPiece piece;
                if(def==null || def.source==null || def.source.Prefab==null || !keys.Add(def.hash) || !project.MappedPieces.TryGetValue(def.hash,out piece) ||
                    piece==null || !ReferenceEquals(pieceDefinition.GetValue(piece),def) || !piece.IsFilled)
                    throw new InvalidOperationException("Missing input "+i+" in completed armor recipe");
                var material=piece.Material;
                if(material!=null && material.Hash==0)throw new InvalidOperationException("Invalid armor ingredient material");
                hashes[i]=material==null?0u:material.Hash;
            }
            return new CraftOrigin{Recipe=recipe.Hash,Materials=hashes,Style=marker.Style,Slot=marker.Slot};
        }
    }
}
