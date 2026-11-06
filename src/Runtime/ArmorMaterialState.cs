using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alta.Chunks;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using Alta.Saving;
using HarmonyLib;
using UnityEngine;
using Stream=Alta.Serialization.Stream;

namespace PlayerArmorMod
{
    // The average is a craft-time snapshot. Native PhysicalMaterial cannot
    // represent a blend, so save it on the actual pickup beside native integrity.
    [SavingInfo("PlayerArmor.MaterialProfile")]
    public sealed class ArmorMaterialState:NetworkEntityBehaviour,ISavable<ArmorMaterialSaveData>
    {
        const uint SaveStructureVersion=1,PayloadSchema=2;
        bool crafted,valid=true;
        float average=1f;
        uint[] hashes=new uint[0];
        uint revision;
        uint layoutVersion=ArmorMaterialPolicy.LegacyBands;
        ArmorPickup marker;
        MethodSync sync;
        static bool installed;
        internal static event Action<ArmorPickup> Changed;
        internal uint Revision{get{return revision;}}
        internal uint LayoutVersion{get{return layoutVersion;}}
        internal uint[] PrimaryMaterialHashes{get{return (uint[])hashes.Clone();}}
        internal bool ReadOldCraftPalette(out uint[] palette,out uint layout)
        {
            palette=crafted&&valid?PrimaryMaterialHashes:null;
            layout=layoutVersion;
            return valid;
        }
        static readonly FieldInfo savingSetups=ArmorNative.Field(typeof(SavingSystem),"savingSetups"),savingTypes=ArmorNative.Field(typeof(SavingSystem),"savingTypes");

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if(installed)return;
            EnsureSaveRegistration();
            harmony.Patch(AccessTools.Method(typeof(SavingSystem),"Reinitialize"),postfix:new HarmonyMethod(typeof(ArmorMaterialState),"EnsureSaveRegistration"));
            installed=true;
        }
        internal static void EnsureSaveRegistration()
        {RegisterSaveTopic(typeof(ArmorMaterialState),typeof(ArmorMaterialSaveData));}

        internal static void RegisterSaveTopic(Type topic,Type dataType)
        {
            // Native Reinitialize scans only its own assembly. A mod-defined
            // SaveStructure needs explicit registration in those same registries.
            var byType=(Dictionary<Type,SavingSystem.SavingSetup>)savingSetups.GetValue(null);
            var byHash=(Dictionary<uint,SavingSystem.SavingSetup>)savingTypes.GetValue(null);
            uint expectedHash=SavingInfoAttribute.GetHash(topic);
            SavingSystem.SavingSetup setup;
            bool exists=byType.TryGetValue(topic,out setup);
            if(exists)
            {
                if(setup==null||setup.Topic!=topic||setup.Hash!=expectedHash||setup.LatestVersion!=SaveStructureVersion||setup.LatestSetup==null||setup.LatestSetup.Type!=dataType)throw new InvalidOperationException("Armor save topic is registered with an incompatible schema: "+topic.Name);
                SavingSystem.SavingSetup mapped;
                if(!byHash.TryGetValue(setup.Hash,out mapped)||!ReferenceEquals(mapped,setup))throw new InvalidOperationException("Armor material native save registries disagree; no registry was modified");
            }
            else
            {
                setup=new SavingSystem.SavingSetup(topic);
                SavingSystem.SavingSetup collision;
                if(byHash.TryGetValue(setup.Hash,out collision))throw new InvalidOperationException("Armor material save topic hash collision with "+collision.Topic.FullName);
            }
            uint version;
            bool hasVersion=SavingSystem.LatestChunkVersioning.HashVersions.TryGetValue(setup.Hash,out version);
            if(hasVersion)
            {if(version!=SaveStructureVersion)throw new InvalidOperationException("Armor material chunk version is incompatible");}
            if(!exists)
            {
                setup.Add(SaveStructureVersion,dataType);setup.FinishInitialization(byType);
                if(setup.Save==null)throw new InvalidOperationException("Native armor material save delegate could not be created");
                byType.Add(topic,setup);byHash.Add(setup.Hash,setup);
            }
            if(!hasVersion)SavingSystem.LatestChunkVersioning.Add(setup.Hash,SaveStructureVersion);
        }
        internal static void AddNative(GameObject root,ArmorPickup owner)
        {
            if(root==null||owner==null||owner.gameObject!=root)throw new ArgumentException("Armor material state requires the pickup's own armor marker");
            EnsureSaveRegistration();
            var state=root.GetComponent<ArmorMaterialState>()??root.AddComponent<ArmorMaterialState>();state.marker=owner;
        }
        public override void Initialize()
        {
            base.Initialize();marker=GetComponent<ArmorPickup>();
            sync=new MethodSync(Entity,EntityMessageType.SyncMaterial,SyncProfile);
            if(marker!=null)
            {
                ArmorDurability.RefreshMaximum(marker);
                RefreshWeight();
            }
        }
        public override void OnConnected(Player player)
        {base.OnConnected(player);if(NetworkSceneManager.IsServer&&sync!=null)sync.SendToPlayer(player);}
        internal static void ApplyCrafted(ArmorPickup owner,float averageMultiplier,uint[] primaryMaterialHashes,uint layoutVersion=ArmorMaterialPolicy.LegacyBands)
        {
            if(!NetworkSceneManager.IsServer)return;
            if(owner==null||!ArmorCatalog.UsesCraftMaterials(owner.Style))throw new ArgumentException("This armor does not accept a crafted material profile");
            if(!ArmorMaterialPolicy.IsValidProfile(averageMultiplier,primaryMaterialHashes)||!ArmorMaterialPolicy.IsValidLayout(layoutVersion))throw new ArgumentException("Invalid crafted armor material profile");
            var state=owner.GetComponent<ArmorMaterialState>();if(state==null)throw new InvalidOperationException("Armor pickup has no material save component");
            state.marker=owner;state.Set(true,true,averageMultiplier,primaryMaterialHashes,layoutVersion);
            // Pre-chunking crafting callbacks run before publication. OnConnected
            // supplies the profile to every initial or later observer.
            if(state.sync!=null&&state.Entity!=null&&state.Entity.Chunk!=null)state.sync.SendToChunks();
        }
        internal static float EffectiveMaximum(ArmorPickup owner,float configured)
        {
            var state=owner==null?null:owner.GetComponent<ArmorMaterialState>();
            return state==null?ArmorMaterialPolicy.EffectiveMaximum(configured,false,false,true,1f)
                :ArmorMaterialPolicy.EffectiveMaximum(configured,UsesMaterialDurability(owner.Style),state.crafted,state.valid,state.average);
        }
        internal static bool UsesMaterialDurability(ArmorStyle style)
        {return style==ArmorStyle.Woodland||ArmorCatalog.UsesMetalMaterials(style);}
        void Set(bool isCrafted,bool isValid,float value,uint[] materialHashes,uint layout)
        {
            bool changed=crafted!=isCrafted||valid!=isValid||average!=value||layoutVersion!=layout||hashes==null||!hashes.SequenceEqual(materialHashes);
            crafted=isCrafted;valid=isValid;average=value;layoutVersion=layout;hashes=(uint[])materialHashes.Clone();
            if(marker!=null)
            {
                ArmorDurability.RefreshMaximum(marker);
                RefreshWeight();
            }
            if(changed)
            {
                unchecked{revision++;}
                var callback=Changed;
                if(callback!=null&&!ReferenceEquals(marker,null))foreach(Action<ArmorPickup> handler in callback.GetInvocationList())
                    try{handler(marker);}catch(Exception error){Core.Error("PLAYER_ARMOR_MATERIAL_VISUAL_FAILED "+error.GetBaseException().Message);}
            }
        }
        void RefreshWeight()
        {
            int count = crafted && valid
                ? ArmorWeightPolicy.PlateCount(marker.Style, (ArmorSlot)marker.Slot, layoutVersion, hashes.Length)
                : 0;
            double total = 0;
            for (int i = 0; i < count; i++)
            {
                var metal = Alta.HashedGeneralValue<PhysicalMaterial>.Get(hashes[i]);
                total += ArmorWeightPolicy.MaterialMultiplier(metal == null ? 1f : metal.WeightMultiplier);
            }
            ArmorWeight.Refresh(marker, count == 0 ? 1f : (float)(total / count));
        }
        internal static void Dispose(){Changed=null;installed=false;}
        void ReadProfile(uint schema,bool isCrafted,float value,uint encodedCount,uint[] fixedHashes)
        {
            // The native save structure remains version 1: field order/types
            // are unchanged. Payload 2 packs its layout into the existing uint.
            uint count=encodedCount,layout=ArmorMaterialPolicy.LegacyBands;
            if(schema==PayloadSchema){count=encodedCount&0xffu;layout=encodedCount>>8;}
            if((schema!=1&&schema!=PayloadSchema)||!ArmorMaterialPolicy.IsValidLayout(layout)||count>ArmorMaterialPolicy.MaximumPrimaryMaterials
                ||(!isCrafted&&(count!=0||value!=1f||layout!=ArmorMaterialPolicy.LegacyBands)))
            {Set(true,false,0f,new uint[0],ArmorMaterialPolicy.LegacyBands);return;}
            if(!isCrafted){Set(false,true,1f,new uint[0],ArmorMaterialPolicy.LegacyBands);return;}
            var selected=fixedHashes.Take((int)count).ToArray();
            if(!ArmorMaterialPolicy.IsValidProfile(value,selected)){Set(true,false,0f,new uint[0],ArmorMaterialPolicy.LegacyBands);return;}
            Set(true,true,value,selected,layout);
        }
        void SyncProfile(IPlayer player,Stream stream)
        {
            // Profile writes belong to the server; client messages can never
            // alter an item's saved factor, even if they know its entity ID.
            if(stream.IsReading&&NetworkSceneManager.IsServer)return;
            uint schema=PayloadSchema,count=(layoutVersion<<8)|(uint)hashes.Length;bool isCrafted=crafted;float value=valid?average:0f;
            var fixedHashes=new uint[ArmorMaterialPolicy.MaximumPrimaryMaterials];Array.Copy(hashes,fixedHashes,hashes.Length);
            stream.SerializeUnsignedInt(ref schema);stream.SerializeBool(ref isCrafted);stream.SerializeFloat(ref value);stream.SerializeUnsignedInt(ref count);
            for(int i=0;i<fixedHashes.Length;i++)stream.SerializeUnsignedInt(ref fixedHashes[i]);
            if(stream.IsReading)ReadProfile(schema,isCrafted,value,count,fixedHashes);
        }
        void ISavable<ArmorMaterialSaveData>.Load(ArmorMaterialSaveData data,ISerializeChildren children)
        {
            if(data==null){Set(true,false,0f,new uint[0],ArmorMaterialPolicy.LegacyBands);return;}
            ReadProfile(data.schemaVersion,data.crafted,data.averageMultiplier,data.primaryCount,data.ReadFixedHashes());
        }
        ArmorMaterialSaveData ISavableSave<ArmorMaterialSaveData>.Save(ISerializeChildren children)
        {
            var data=new ArmorMaterialSaveData{schemaVersion=PayloadSchema,crafted=crafted,averageMultiplier=valid?average:0f,primaryCount=(layoutVersion<<8)|(uint)hashes.Length};
            data.WriteHashes(hashes);return data;
        }
    }

    [SaveStructure(typeof(ArmorMaterialState),1u)]
    public sealed class ArmorMaterialSaveData:NewSaveData<ArmorMaterialState,ArmorMaterialSaveData>
    {
        public uint schemaVersion=2;
        public bool crafted;
        public float averageMultiplier=1f;
        public uint primaryCount=ArmorMaterialPolicy.LegacyBands<<8;
        // Payload 1 used a raw count. Payload 2 stores count in the low byte
        // and layout in the upper bits. No native serialized member was added.
        // Fixed slots bound native save deserialization before validation. The
        // native generic array serializer allocates its file-supplied length.
        public uint m00,m01,m02,m03,m04,m05,m06,m07,m08,m09,m10,m11,m12,m13,m14,m15,
            m16,m17,m18,m19,m20,m21,m22,m23,m24,m25,m26,m27,m28,m29,m30,m31;
        static readonly FieldInfo[] slots=Enumerable.Range(0,ArmorMaterialPolicy.MaximumPrimaryMaterials)
            .Select(index=>typeof(ArmorMaterialSaveData).GetField("m"+index.ToString("D2"))).ToArray();
        internal uint[] ReadFixedHashes(){return slots.Select(field=>(uint)field.GetValue(this)).ToArray();}
        internal void WriteHashes(uint[] value){for(int i=0;i<value.Length;i++)slots[i].SetValue(this,value[i]);}
    }
}
