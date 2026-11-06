using System;
using System.Linq;
using Alta;
using Alta.Pages;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static partial class ArmorCrafting
    {
        // Approved recipe ink takes precedence for all thirty-five pieces.
        // The four surviving native sprites remain safe artwork fallbacks.
        static Sprite RecipeIcon(ArmorPieceDefinition piece,Mesh mesh)
        {
            if(piece==null)throw new ArgumentNullException("piece");
            Sprite approved;if(ArmorRecipeGlyphArt.TryGet(piece,out approved))return approved;
            uint nativeHash=0;
            if(piece.Style==ArmorStyle.Metal||piece.Style==ArmorStyle.Leather)
            {
                if(piece.Slot==ArmorSlot.Head)nativeHash=piece.Style==ArmorStyle.Leather?26434u:25744u;
                else if(piece.Slot==ArmorSlot.Chest)nativeHash=piece.Style==ArmorStyle.Leather?27136u:27362u;
            }
            if(nativeHash!=0)
            {
                var native=HashedGeneralValue<Glyph>.All.FirstOrDefault(glyph=>glyph!=null&&glyph.Hash==nativeHash);
                if(native!=null&&native.Sprite!=null)return native.Sprite;
            }
            // Other pieces, or unavailable native sprites, retain their actual
            // mesh silhouette. Native/result/title identities stay put.
            return ArmorCraftingMesh.Icon(mesh);
        }
    }
}
