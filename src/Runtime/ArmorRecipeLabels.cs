using System;
using System.Collections.Generic;
using System.Linq;
using Alta.Crafting;
using Alta.Global;
using Alta.Pages;
using HarmonyLib;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static partial class ArmorCrafting
    {
        static readonly Dictionary<string,Glyph> recipeLabels=new Dictionary<string,Glyph>();
        internal const float RecipeLabelIngredientOffset=.06f,RecipeLabelScale=4.5f;
        internal static readonly Vector2 RecipeLabelPosition=new Vector2(.4f,1.09f);
        static void InstallRecipeLabelHooks(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(GenericCraftingRecipePageProvider),"GeneratePage",new[]{typeof(GenericCraftingRecipe)}),
                postfix:new HarmonyMethod(typeof(ArmorCrafting),"LabelArmorRecipePage"));
        }
        static void RegisterRecipeLabel(ArmorPieceDefinition piece)
        {
            if(recipeLabels.ContainsKey(piece.PrefabName))return;
            uint hash=ArmorCraftingPlan.Hash16("PlayerArmor/RecipeTitle/"+piece.PrefabName);CheckHash<Glyph>(hash);
            var glyph=ScriptableObject.CreateInstance<Glyph>();glyph.name=ArmorRecipeLabelArt.FullTitle(piece);
            ArmorNative.Set(glyph,"hash",(int)hash);
#if CLIENT
            var texture=new Texture2D(ArmorRecipeLabelArt.Size,ArmorRecipeLabelArt.Size,TextureFormat.RGBA32,false)
                {name=glyph.name+" ink",hideFlags=HideFlags.DontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            texture.SetPixels32(ArmorRecipeLabelArt.Pixels(piece));texture.Apply(false,true);
            var sprite=Sprite.Create(texture,new Rect(0,0,ArmorRecipeLabelArt.Size,ArmorRecipeLabelArt.Size),new Vector2(.5f,.5f),100);
            sprite.name=glyph.name;ArmorNative.Set(glyph,"sprite",sprite);
#endif
            AddValue(glyph);recipeLabels.Add(piece.PrefabName,glyph);
        }
        static void LabelArmorRecipePage(GenericCraftingRecipe recipe,PageData __result)
        {
            if(__result==null||!OwnsRecipe(recipe)||!ReferenceEquals(__result.ProviderData,recipe))return;
            Glyph label;if(!recipeLabels.TryGetValue(versions[recipe.Hash].Piece.PrefabName,out label))return;
            if(__result.Glyphs.Any(g=>ReferenceEquals(g.Glyph,label)))return;
            // Native pages have a sprite-only canvas. Make room within this
            // page's own ingredient rows; never edit shared page settings,
            // source/result glyphs, recipe hashes or ingredient quantities.
            var slots=GlobalSettings<GenericCraftingRecipePageSettings>.Instance.materialPositions;
            for(int i=0;i<__result.Glyphs.Count;i++)
            {
                var detail=__result.Glyphs[i];
                if(!slots.Any(slot=>(slot.position-detail.Position).sqrMagnitude<.000001f))continue;
                __result.Glyphs[i]=new Glyph.Positioning(detail.Glyph,detail.Position-new Vector2(0,RecipeLabelIngredientOffset),detail.Rotation,detail.Scale);
            }
            __result.Glyphs.Add(new Glyph.Positioning(label,RecipeLabelPosition,0,RecipeLabelScale));
        }
    }

    // A small, deterministic ink alphabet. The native page renderer bakes this
    // sprite into its regular lit page material; no world text/shader is added.
    internal static class ArmorRecipeLabelArt
    {
        internal const int Size=256;
        static readonly Dictionary<char,string> alphabet=new Dictionary<char,string>{
            {'A',"01110/10001/10001/11111/10001/10001/10001"},{'B',"11110/10001/10001/11110/10001/10001/11110"},
            {'C',"01111/10000/10000/10000/10000/10000/01111"},{'D',"11110/10001/10001/10001/10001/10001/11110"},
            {'E',"11111/10000/10000/11110/10000/10000/11111"},{'F',"11111/10000/10000/11110/10000/10000/10000"},
            {'G',"01111/10000/10000/10111/10001/10001/01111"},{'H',"10001/10001/10001/11111/10001/10001/10001"},
            {'I',"11111/00100/00100/00100/00100/00100/11111"},{'J',"00111/00010/00010/00010/10010/10010/01100"},
            {'K',"10001/10010/10100/11000/10100/10010/10001"},{'L',"10000/10000/10000/10000/10000/10000/11111"},
            {'M',"10001/11011/10101/10101/10001/10001/10001"},{'N',"10001/11001/10101/10011/10001/10001/10001"},
            {'O',"01110/10001/10001/10001/10001/10001/01110"},{'P',"11110/10001/10001/11110/10000/10000/10000"},
            {'Q',"01110/10001/10001/10001/10101/10010/01101"},{'R',"11110/10001/10001/11110/10100/10010/10001"},
            {'S',"01111/10000/10000/01110/00001/00001/11110"},{'T',"11111/00100/00100/00100/00100/00100/00100"},
            {'U',"10001/10001/10001/10001/10001/10001/01110"},{'V',"10001/10001/10001/10001/10001/01010/00100"},
            {'W',"10001/10001/10001/10101/10101/11011/10001"},{'X',"10001/10001/01010/00100/01010/10001/10001"},
            {'Y',"10001/10001/01010/00100/00100/00100/00100"},{'Z',"11111/00001/00010/00100/01000/10000/11111"}
        };
        internal static string Header(ArmorStyle style)
        {switch(style){case ArmorStyle.Metal:return "METAL ARMOR";case ArmorStyle.Leather:return "LEATHER ARMOR";case ArmorStyle.Woodland:return "WOODLAND ARMOR";case ArmorStyle.Hebios:return "HEBIOS ARMOR";case ArmorStyle.Mushroom:return "MUSHROOM ARMOR";default:throw new ArgumentOutOfRangeException("style");}}
        internal static string Part(ArmorSlot slot)
        {
            switch(slot)
            {
                case ArmorSlot.Head:return "HELMET";case ArmorSlot.Chest:return "CUIRASS";
                case ArmorSlot.LeftShoulder:return "LEFT PAULDRON";case ArmorSlot.RightShoulder:return "RIGHT PAULDRON";
                case ArmorSlot.LeftForearm:return "LEFT BRACER";case ArmorSlot.RightForearm:return "RIGHT BRACER";
                case ArmorSlot.Hips:return "TASSETS";default:throw new ArgumentOutOfRangeException("slot");
            }
        }
        internal static string FullTitle(ArmorPieceDefinition piece){return Header(piece.Style)+" - "+Part(piece.Slot);}
        internal static Color32[] Pixels(ArmorPieceDefinition piece)
        {
            var pixels=new Color32[Size*Size];const int mainScale=2,styleScale=1,gap=3;
            int mainBottom=(Size-(7*mainScale+gap+7*styleScale))/2;
            Draw(pixels,Part(piece.Slot),mainScale,mainBottom);
            Draw(pixels,Header(piece.Style),styleScale,mainBottom+7*mainScale+gap);return pixels;
        }
        static void Draw(Color32[] pixels,string text,int scale,int bottom)
        {
            int left=(Size-(text.Length*6-1)*scale)/2;
            foreach(char letter in text)
            {
                if(letter!=' ')
                {
                    string bits=alphabet[letter];
                    for(int row=0;row<7;row++)for(int column=0;column<5;column++)if(bits[row*6+column]=='1')
                        for(int y=0;y<scale;y++)for(int x=0;x<scale;x++)
                            pixels[(bottom+(6-row)*scale+y)*Size+left+column*scale+x]=new Color32(35,23,14,255);
                }
                left+=6*scale;
            }
        }
    }
}
