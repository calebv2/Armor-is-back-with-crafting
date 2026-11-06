using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PlayerArmorMod
{
    public enum ArmorSlot : byte
    {
        Head=0,
        Chest=1,
        LeftShoulder=2,
        RightShoulder=3,
        LeftForearm=4,
        RightForearm=5,
        Hips=6
    }

    public enum ArmorStyle : byte
    {
        Metal=0,
        Leather=1,
        Woodland=2,
        Hebios=3,
        Mushroom=4,
        Wyrm=5
    }

    public sealed class ArmorPieceDefinition
    {
        public readonly ArmorStyle Style;
        public readonly ArmorSlot Slot;
        public readonly string PrefabName;
        public readonly string BoneName;
        public readonly string ArtRootName;

        internal ArmorPieceDefinition(ArmorSlot slot,string prefabName,string boneName,string artRootName)
            :this(ArmorStyle.Metal,slot,prefabName,boneName,artRootName)
        {
        }
        internal ArmorPieceDefinition(ArmorStyle style,ArmorSlot slot,string prefabName,string boneName,string artRootName)
        {
            Style=style;Slot=slot;PrefabName=prefabName;BoneName=boneName;ArtRootName=artRootName;
        }
    }

    public static class ArmorCatalog
    {
        public const int SlotCount=7;
        public const byte AllEquippedMask=127;
        // Presentation-only wire state. Saved armor remains the native docked
        // pickup identity; protocol15 widens the transient family mask to allow
        // five families without renumbering any existing style or item.
        public const int StyleBitsPerSlot=3;
        public const uint StyleValueMask=7;
        public const uint AllStyleBits=(1u<<(StyleBitsPerSlot*SlotCount))-1u;
        public const string HarnessName="ATT_PlayerArmor_Harness";

        static readonly ReadOnlyCollection<ArmorPieceDefinition> pieces=Array.AsReadOnly(new[]
        {
            new ArmorPieceDefinition(ArmorSlot.Head,"ATT_IronArmor_Helmet","head_jnt","Armor_Head"),
            new ArmorPieceDefinition(ArmorSlot.Chest,"ATT_IronArmor_Cuirass","chest_jnt","Armor_Chest"),
            new ArmorPieceDefinition(ArmorSlot.LeftShoulder,"ATT_IronArmor_Pauldron_Left","shoulder_L_jnt","Armor_LeftShoulder"),
            new ArmorPieceDefinition(ArmorSlot.RightShoulder,"ATT_IronArmor_Pauldron_Right","shoulder_R_jnt","Armor_RightShoulder"),
            new ArmorPieceDefinition(ArmorSlot.LeftForearm,"ATT_IronArmor_Bracer_Left","forearm_L_jnt","Armor_LeftForearm"),
            new ArmorPieceDefinition(ArmorSlot.RightForearm,"ATT_IronArmor_Bracer_Right","forearm_R_jnt","Armor_RightForearm"),
            new ArmorPieceDefinition(ArmorSlot.Hips,"ATT_IronArmor_Tassets","hipBase_jnt","Armor_Hips")
        });

        static readonly ReadOnlyCollection<ArmorPieceDefinition> leatherPieces=Array.AsReadOnly(new[]
        {
            new ArmorPieceDefinition(ArmorStyle.Leather,ArmorSlot.Head,"ATT_LeatherArmor_Helmet","head_jnt","Leather/Armor_Head"),
            new ArmorPieceDefinition(ArmorStyle.Leather,ArmorSlot.Chest,"ATT_LeatherArmor_Cuirass","chest_jnt","Leather/Armor_Chest"),
            new ArmorPieceDefinition(ArmorStyle.Leather,ArmorSlot.LeftShoulder,"ATT_LeatherArmor_Pauldron_Left","shoulder_L_jnt","Leather/Armor_LeftShoulder"),
            new ArmorPieceDefinition(ArmorStyle.Leather,ArmorSlot.RightShoulder,"ATT_LeatherArmor_Pauldron_Right","shoulder_R_jnt","Leather/Armor_RightShoulder"),
            new ArmorPieceDefinition(ArmorStyle.Leather,ArmorSlot.LeftForearm,"ATT_LeatherArmor_Bracer_Left","forearm_L_jnt","Leather/Armor_LeftForearm"),
            new ArmorPieceDefinition(ArmorStyle.Leather,ArmorSlot.RightForearm,"ATT_LeatherArmor_Bracer_Right","forearm_R_jnt","Leather/Armor_RightForearm"),
            new ArmorPieceDefinition(ArmorStyle.Leather,ArmorSlot.Hips,"ATT_LeatherArmor_Tassets","hipBase_jnt","Leather/Armor_Hips")
        });
        static readonly ReadOnlyCollection<ArmorPieceDefinition> woodlandPieces=MakeFamily(ArmorStyle.Woodland,"ATT_WoodlandArmor_","Woodland/");
        static readonly ReadOnlyCollection<ArmorPieceDefinition> hebiosPieces=MakeFamily(ArmorStyle.Hebios,"ATT_HebiosArmor_","Hebios/");
        static readonly ReadOnlyCollection<ArmorPieceDefinition> mushroomPieces=MakeFamily(ArmorStyle.Mushroom,"ATT_MushroomArmor_","Mushroom/");
        static readonly ReadOnlyCollection<ArmorPieceDefinition> wyrmPieces=MakeFamily(ArmorStyle.Wyrm,"ATT_WyrmArmor_","Wyrm/");
        static readonly ReadOnlyCollection<ArmorPieceDefinition> legacyPieces=MakePieces(false);
        static readonly ReadOnlyCollection<ArmorPieceDefinition> expansionPieces=MakeExpansions();
        // Mushroom pages and plans remain readable for old saves. Wyrm was
        // introduced as loot and has never had workbench pages or jig plans.
        static readonly ReadOnlyCollection<ArmorPieceDefinition> recipePagePieces=MakePieces(true);
        static readonly ReadOnlyCollection<ArmorPieceDefinition> allPieces=AddWyrmPieces();

        static ReadOnlyCollection<ArmorPieceDefinition> MakeFamily(ArmorStyle style,string prefix,string artPrefix)
        {
            var result=new ArmorPieceDefinition[SlotCount];
            for(int i=0;i<result.Length;i++)
            {
                var original=pieces[i];string suffix=original.PrefabName.Substring("ATT_IronArmor_".Length);
                result[i]=new ArmorPieceDefinition(style,original.Slot,prefix+suffix,original.BoneName,artPrefix+original.ArtRootName);
            }
            return Array.AsReadOnly(result);
        }
        static ReadOnlyCollection<ArmorPieceDefinition> MakePieces(bool includeExpansions)
        {
            var all=new List<ArmorPieceDefinition>(pieces);
            all.AddRange(leatherPieces);
            if(includeExpansions){all.AddRange(woodlandPieces);all.AddRange(hebiosPieces);all.AddRange(mushroomPieces);}
            return all.AsReadOnly();
        }
        static ReadOnlyCollection<ArmorPieceDefinition> MakeExpansions()
        {var result=new List<ArmorPieceDefinition>(woodlandPieces);result.AddRange(hebiosPieces);result.AddRange(mushroomPieces);result.AddRange(wyrmPieces);return result.AsReadOnly();}
        static ReadOnlyCollection<ArmorPieceDefinition> AddWyrmPieces()
        {
            var result=new List<ArmorPieceDefinition>(recipePagePieces);
            result.AddRange(wyrmPieces);
            return result.AsReadOnly();
        }
        public static ReadOnlyCollection<ArmorPieceDefinition> Pieces {get{return pieces;}}
        public static ReadOnlyCollection<ArmorPieceDefinition> LeatherPieces {get{return leatherPieces;}}
        public static ReadOnlyCollection<ArmorPieceDefinition> WoodlandPieces {get{return woodlandPieces;}}
        public static ReadOnlyCollection<ArmorPieceDefinition> HebiosPieces {get{return hebiosPieces;}}
        public static ReadOnlyCollection<ArmorPieceDefinition> MushroomPieces {get{return mushroomPieces;}}
        public static ReadOnlyCollection<ArmorPieceDefinition> WyrmPieces {get{return wyrmPieces;}}
        public static ReadOnlyCollection<ArmorPieceDefinition> LegacyPieces {get{return legacyPieces;}}
        public static ReadOnlyCollection<ArmorPieceDefinition> ExpansionPieces {get{return expansionPieces;}}
        public static ReadOnlyCollection<ArmorPieceDefinition> AllPieces {get{return allPieces;}}
        public static ReadOnlyCollection<ArmorPieceDefinition> RecipePagePieces {get{return recipePagePieces;}}
        public static bool IsValidSlot(ArmorSlot slot){return (int)slot>=0&&(int)slot<SlotCount;}
        public static bool IsValidStyle(ArmorStyle style){return (int)style<=(int)ArmorStyle.Wyrm;}
        public static bool UsesMetalMaterials(ArmorStyle style){return style==ArmorStyle.Metal||style==ArmorStyle.Hebios;}
        public static bool UsesCraftMaterials(ArmorStyle style){return UsesMetalMaterials(style)||style==ArmorStyle.Woodland||style==ArmorStyle.Leather;}
        public static bool IsChestOnly(ArmorStyle style){return style==ArmorStyle.Mushroom;}
        public static bool IsLootOnly(ArmorStyle style){return IsChestOnly(style)||style==ArmorStyle.Wyrm;}
        public static bool TintsFasteners(ArmorStyle style){return UsesMetalMaterials(style)||style==ArmorStyle.Leather;}
        public static ReadOnlyCollection<ArmorPieceDefinition> ForStyle(ArmorStyle style)
        {
            switch(style){case ArmorStyle.Metal:return pieces;case ArmorStyle.Leather:return leatherPieces;case ArmorStyle.Woodland:return woodlandPieces;case ArmorStyle.Hebios:return hebiosPieces;case ArmorStyle.Mushroom:return mushroomPieces;case ArmorStyle.Wyrm:return wyrmPieces;default:throw new ArgumentOutOfRangeException("style");}
        }
        public static uint PackLegacyLeather(byte leatherMask)
        {uint result=0;for(int i=0;i<SlotCount;i++)if((leatherMask&(1<<i))!=0)result=SetStyle(result,(ArmorSlot)i,ArmorStyle.Leather);return result;}
        public static ArmorStyle StyleAt(uint styles,ArmorSlot slot)
        {if(!IsValidSlot(slot))throw new ArgumentOutOfRangeException("slot");return (ArmorStyle)((styles>>(StyleBitsPerSlot*(int)slot))&StyleValueMask);}
        public static uint SetStyle(uint styles,ArmorSlot slot,ArmorStyle style)
        {
            if(!IsValidSlot(slot))throw new ArgumentOutOfRangeException("slot");
            if(!IsValidStyle(style))throw new ArgumentOutOfRangeException("style");
            int shift=StyleBitsPerSlot*(int)slot;
            return(styles&~(StyleValueMask<<shift))|((uint)style<<shift);
        }
        public static byte LeatherMaskOf(uint styles)
        {byte result=0;for(int i=0;i<SlotCount;i++)if(StyleAt(styles,(ArmorSlot)i)==ArmorStyle.Leather)result|=(byte)(1<<i);return result;}
        public static bool ValidStyles(byte occupied,uint styles)
        {
            if(occupied>AllEquippedMask||(styles&~AllStyleBits)!=0)return false;
            for(int i=0;i<SlotCount;i++)
            {var style=StyleAt(styles,(ArmorSlot)i);if(!IsValidStyle(style)||((occupied&(1<<i))==0&&style!=ArmorStyle.Metal))return false;}
            return true;
        }
        public static ArmorPieceDefinition Get(ArmorSlot slot)
        {return Get(ArmorStyle.Metal,slot);}
        public static ArmorPieceDefinition Get(ArmorStyle style,ArmorSlot slot)
        {
            if(!IsValidStyle(style))throw new ArgumentOutOfRangeException("style");
            if(!IsValidSlot(slot))throw new ArgumentOutOfRangeException("slot");
            return ForStyle(style)[(int)slot];
        }
        public static byte SlotMask(ArmorSlot slot)
        {
            if(!IsValidSlot(slot))throw new ArgumentOutOfRangeException("slot");
            return (byte)(1<<(int)slot);
        }
        public static bool IsEquipped(byte mask,ArmorSlot slot)
        {
            return IsValidSlot(slot)&&(mask&SlotMask(slot))!=0;
        }
    }
}
