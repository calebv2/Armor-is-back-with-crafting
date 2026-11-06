using System;
using System.Linq;
using System.Reflection;
using Alta.Saving;
using Alta.Networking;
using Alta.Chunks;
using HarmonyLib;
using UnityEngine;

namespace PlayerArmorMod
{
    // A separate native save topic keeps pre-existing metal-profile saves readable.
    [SavingInfo("PlayerArmor.CraftOrigin")]
    public sealed class ArmorCraftRecord : MonoBehaviour, ISavable<ArmorCraftRecordData>
    {
        internal const int MaxPieces = 32;
        const uint InvalidRecipe = uint.MaxValue;
        uint recipe;
        uint[] materials = new uint[0];
        static bool installed;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if(installed) return;
            Register();
            harmony.Patch(AccessTools.Method(typeof(SavingSystem),"Reinitialize"),postfix:new HarmonyMethod(typeof(ArmorCraftRecord),"Register"));
            installed=true;
        }

        internal static void Register(){ArmorMaterialState.RegisterSaveTopic(typeof(ArmorCraftRecord),typeof(ArmorCraftRecordData));}
        internal static void Dispose(){installed=false;}
        internal static void AddNative(GameObject root)
        {
            Register();
            if(root.GetComponent<ArmorCraftRecord>()==null)root.AddComponent<ArmorCraftRecord>();
        }

        internal bool HasRecord { get { return recipe!=0; } }

        internal bool TryRead(out uint hash,out uint[] hashes)
        {
            hash=0; hashes=null;
            if(recipe==0 || recipe>=ushort.MaxValue || materials==null || materials.Length<1 || materials.Length>MaxPieces)return false;
            hash=recipe; hashes=(uint[])materials.Clone(); return true;
        }

        internal void Store(uint hash,uint[] hashes)
        {
            if(hash==0 || hash>=ushort.MaxValue || hashes==null || hashes.Length<1 || hashes.Length>MaxPieces)
                throw new ArgumentException("Invalid armor craft record");
            recipe=hash; materials=(uint[])hashes.Clone();
        }

        ArmorCraftRecordData ISavableSave<ArmorCraftRecordData>.Save(ISerializeChildren children)
        {
            var data=new ArmorCraftRecordData{recipeHash=recipe,count=(uint)(materials==null?0:materials.Length)};
            if(materials!=null) data.Write(materials);
            return data;
        }

        void ISavable<ArmorCraftRecordData>.Load(ArmorCraftRecordData data,ISerializeChildren children)
        {
            materials=new uint[0];
            if(data!=null && data.recipeHash==0 && data.count==0){recipe=0;return;}
            // A corrupt recorded item must never acquire the more permissive legacy fallback.
            recipe=InvalidRecipe;
            if(data==null || data.recipeHash==0 || data.recipeHash>=ushort.MaxValue || data.count<1 || data.count>MaxPieces)return;
            Store(data.recipeHash,data.Read((int)data.count));
        }
    }

    [SaveStructure(typeof(ArmorCraftRecord),1u)]
    public sealed class ArmorCraftRecordData : NewSaveData<ArmorCraftRecord,ArmorCraftRecordData>
    {
        public uint recipeHash,count;
        // Fixed-width slots keep an invalid on-disk count from allocating an arbitrary array.
        public uint m00,m01,m02,m03,m04,m05,m06,m07,m08,m09,m10,m11,m12,m13,m14,m15,
                    m16,m17,m18,m19,m20,m21,m22,m23,m24,m25,m26,m27,m28,m29,m30,m31;
        static readonly FieldInfo[] Slots=Enumerable.Range(0,ArmorCraftRecord.MaxPieces)
            .Select(i=>typeof(ArmorCraftRecordData).GetField("m"+i.ToString("D2",System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        internal uint[] Read(int n){return Slots.Take(n).Select(f=>(uint)f.GetValue(this)).ToArray();}
        internal void Write(uint[] values){for(int i=0;i<values.Length;i++)Slots[i].SetValue(this,values[i]);}
    }
}
