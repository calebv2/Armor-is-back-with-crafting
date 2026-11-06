using System;
using System.Collections.Generic;
using System.Reflection;
using Alta.Customization;

namespace PlayerArmorMod
{
    // Presentation only: the wardrobe, its saved arguments and native slot
    // membership are never edited. Native logical parts own packing and LODs.
    internal sealed class ArmorCosmeticCoverage : IDisposable
    {
        sealed class Overlay
        {
            internal CustomizationArguments Packed;
            internal TwoColors Colors;
            internal CosmeticPacker.Input FabricInput;
        }
        sealed class Owned
        {
            internal bool UsesNativeFit;
            internal readonly Dictionary<ICustomizationHandler,Overlay> Overlays = new Dictionary<ICustomizationHandler,Overlay>();
        }
        static readonly ICustomizationHandler[] NoHandlers = new ICustomizationHandler[0];
        static readonly FieldInfo ManagerField = typeof(PlayerCharacterCustomizationManager).GetField("internalManager",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly FieldInfo ItemsField = typeof(CustomizationVisualManager).GetField("items",BindingFlags.Instance|BindingFlags.NonPublic);
        readonly Dictionary<CustomizationSlotEntry,Owned> owned = new Dictionary<CustomizationSlotEntry,Owned>();
        readonly HashSet<CustomizationSlotEntry> current = new HashSet<CustomizationSlotEntry>();
        readonly List<CustomizationSlotEntry> stale = new List<CustomizationSlotEntry>();
        readonly Action<string> log;
        PlayerCharacterCustomizationManager manager;
        bool warned,disposed;

        internal ArmorCosmeticCoverage(Action<string> logger=null) { log=logger; }

        internal void Update(PlayerCharacterCustomizationManager next,uint armorMask,bool enabled)
        {
            if(disposed)return;
            if(!ReferenceEquals(manager,next)){Clear();manager=next;}
            if(manager==null)return;
            try
            {
                ReadEntries(manager,current);
                var handlers=ReadHandlers(manager);
                uint mask=enabled?armorMask&127u:0u;
                stale.Clear();
                foreach(var pair in owned)
                {
                    bool nativeFit=Covered(pair.Key,mask)&&CanCompact(pair.Key);
                    if(!current.Contains(pair.Key)||(pair.Value.UsesNativeFit!=nativeFit)||
                        (!nativeFit&&!CanFitFabric(pair.Key,mask)))stale.Add(pair.Key);
                }
                foreach(var entry in stale){Release(entry,owned[entry],current.Contains(entry),handlers);owned.Remove(entry);}
                if(mask==0)return;
                foreach(var entry in current)
                {
                    bool nativeFit=Covered(entry,mask)&&CanCompact(entry);
                    if(!nativeFit&&!CanFitFabric(entry,mask))continue;
                    Owned state;
                    if(!owned.TryGetValue(entry,out state))
                    {
                        state=new Owned{UsesNativeFit=nativeFit};
                        owned.Add(entry,state);
                    }
                    if(nativeFit)ApplyCompact(entry,state,handlers);
                    else ApplyFabric(entry,state,handlers);
                }
            }
            catch(Exception error)
            {
                // A customization rebuild can replace the manager/packer during
                // a frame. Keep retrying; do not mutate wardrobe/save data.
                if(!warned&&log!=null){warned=true;log("Cosmetic armor coverage deferred: "+error.GetBaseException().Message);}
            }
        }

        static void ReadEntries(PlayerCharacterCustomizationManager source,HashSet<CustomizationSlotEntry> result)
        {
            result.Clear();
            if(ManagerField==null||ItemsField==null)throw new MissingFieldException("Native customization entry layout changed");
            var native=ManagerField.GetValue(source) as CustomizationVisualManager;
            if(native==null)throw new InvalidOperationException("Unsupported native cosmetic visual manager");
            var map=ItemsField.GetValue(native) as Dictionary<uint,List<CustomizationSlotEntry>>;
            if(map==null)throw new InvalidOperationException("Native cosmetic entries unavailable");
            foreach(var list in map.Values)foreach(var entry in list)if(entry!=null)result.Add(entry);
        }
        static ICustomizationHandler[] ReadHandlers(PlayerCharacterCustomizationManager source)
        {
            var result=new List<ICustomizationHandler>();
            foreach(var handler in source.Handlers)if(handler!=null&&!handler.IsDestroyed)result.Add(handler);
            return result.ToArray();
        }
        static bool IsClothing(CustomizationSlotEntry entry)
        {
            var mesh=entry.Part as ICosmeticMeshPartBase;
            if(mesh==null||entry.Part is SkinMeshPart||mesh.IsTextureSharingForced||entry.Visual==null
                ||!(entry.Visual.Definition is PlayerCosmeticDefinition)||entry.Part.Slots==null)return false;
            foreach(int slot in entry.Part.Slots)
                if(slot>=301&&slot!=331&&slot!=501&&slot!=511)return false;
            return true;
        }
        internal static bool CoversSlot(uint mask,int slot)
        {
            if((mask&1)!=0&&(Between(slot,201,213)||Between(slot,251,263)||slot==331))return true;
            if((mask&2)!=0&&(Between(slot,11,55)||Between(slot,181,195)||Between(slot,281,285)||slot==511))return true;
            if((mask&4)!=0&&(Between(slot,81,85)||Between(slot,101,105)))return true;
            if((mask&8)!=0&&(Between(slot,91,95)||Between(slot,111,115)))return true;
            if((mask&16)!=0&&(Between(slot,121,125)||Between(slot,141,145)||Between(slot,161,165)))return true;
            if((mask&32)!=0&&(Between(slot,131,135)||Between(slot,151,155)||Between(slot,171,175)))return true;
            return(mask&64)!=0&&(Between(slot,51,75)||slot==501);
        }
        static bool Between(int value,int first,int last){return value>=first&&value<=last;}
        static bool Covered(CustomizationSlotEntry entry,uint mask)
        {
            if(!IsClothing(entry))return false;
            foreach(int slot in entry.Part.Slots)if(CoversSlot(mask,slot))return true;
            return false;
        }
        static bool HasCompact(ICosmeticMeshPartBase part)
        {
            var normal=part.Lods;var compact=part.ImpededLods;
            if(normal==null||normal.Length==0||compact==null||compact.Length<normal.Length)return false;
            bool any=false;
            for(int i=0;i<normal.Length;i++)if(normal[i]!=null){any=true;if(compact[i]==null)return false;}
            return any;
        }
        static bool CanCompact(CustomizationSlotEntry entry)
        {return entry.Arguments is TwoColors&&entry.Part is ICosmeticMeshPartBase&&HasCompact((ICosmeticMeshPartBase)entry.Part);}
        static bool CanFitFabric(CustomizationSlotEntry entry,uint mask)
        {
            if((mask&2u)==0||!(entry.Arguments is TwoColors)||!IsClothing(entry))return false;
            // Limit the generated fit to torso garments. A sash, belt, hat or
            // arm-only cosmetic needs its own geometry rules.
            foreach(int slot in entry.Part.Slots)
                if(Between(slot,21,45)||Between(slot,191,195))return true;
            return false;
        }
        static bool TryInput(CustomizationSlotEntry entry,ICustomizationHandler handler,out CosmeticPacker.Input input)
        {
            input=null;
            try
            {
                var mesh=entry.Part as ICosmeticMeshPartBase;
                return mesh!=null&&handler!=null&&!handler.IsDestroyed&&handler.CharacterDummy!=null
                    &&handler.CharacterDummy.CosmeticPacker!=null
                    &&handler.CharacterDummy.CosmeticPacker.Inputs.TryGetValue(mesh.PartId,out input)&&input!=null;
            }
            catch(Exception){return false;}
        }
        static bool Contains(ICustomizationHandler[] handlers,ICustomizationHandler handler)
        {foreach(var candidate in handlers)if(ReferenceEquals(candidate,handler))return true;return false;}
        static void Prune(Owned state,ICustomizationHandler[] handlers)
        {
            var remove=new List<ICustomizationHandler>();
            foreach(var handler in state.Overlays.Keys)if(!Contains(handlers,handler))remove.Add(handler);
            foreach(var handler in remove)
            {
                var input=state.Overlays[handler].FabricInput;
                if(input!=null)ArmorFabricMeshPacking.Remove(input);
                state.Overlays.Remove(handler);
            }
        }
        static void ApplyFabric(CustomizationSlotEntry entry,Owned state,ICustomizationHandler[] handlers)
        {
            Prune(state,handlers);
            foreach(var handler in handlers)
            {
                state.Overlays.TryGetValue(handler,out var overlay);
                CosmeticPacker.Input input=null;
                bool canFit=entry.DisablerCount==0&&TryInput(entry,handler,out input)&&
                    input.Arguments!=null&&!input.Arguments.IsImpeded;
                if(overlay!=null)
                {
                    if(canFit&&ReferenceEquals(overlay.FabricInput,input))continue;
                    ArmorFabricMeshPacking.Remove(overlay.FabricInput);
                    state.Overlays.Remove(handler);
                }
                if(!canFit||!ArmorFabricMeshPacking.Set(input))continue;
                state.Overlays.Add(handler,new Overlay{FabricInput=input});
                handler.CharacterDummy.CosmeticPacker.PackAndApply();
            }
        }
        static void ApplyCompact(CustomizationSlotEntry entry,Owned state,ICustomizationHandler[] handlers)
        {
            Prune(state,handlers);
            if(entry.DisablerCount>0)return;
            var original=entry.Arguments as TwoColors;if(original==null)return;
            foreach(var handler in handlers)
            {
                CosmeticPacker.Input input;if(!TryInput(entry,handler,out input))continue;
                Overlay overlay;state.Overlays.TryGetValue(handler,out overlay);
                // Native cosmetics may already select their compact geometry.
                if(original.IsImpeded)
                {
                    if(overlay!=null&&ReferenceEquals(input.Arguments,overlay.Packed))entry.Part.ChangeArguments(new[]{handler},original);
                    state.Overlays.Remove(handler);continue;
                }
                if(overlay!=null&&ReferenceEquals(input.Arguments,overlay.Packed)
                    &&ReferenceEquals(overlay.Colors.Primary,original.Primary)&&ReferenceEquals(overlay.Colors.Secondary,original.Secondary))continue;
                var copy=new TwoColors{Primary=original.Primary,Secondary=original.Secondary,IsImpeded=true};
                entry.Part.ChangeArguments(new[]{handler},copy);
                state.Overlays[handler]=new Overlay{Colors=copy,Packed=copy};
            }
        }
        static void Release(CustomizationSlotEntry entry,Owned state,bool registered,ICustomizationHandler[] handlers)
        {
            if(!state.UsesNativeFit)
            {
                foreach(var pair in state.Overlays)
                {
                    ArmorFabricMeshPacking.Remove(pair.Value.FabricInput);
                    if(registered&&entry.DisablerCount==0&&Contains(handlers,pair.Key)&&
                        TryInput(entry,pair.Key,out var input)&&ReferenceEquals(input,pair.Value.FabricInput))
                        pair.Key.CharacterDummy.CosmeticPacker.PackAndApply();
                }
                state.Overlays.Clear();
                return;
            }
            if(registered&&entry.Part!=null&&entry.Arguments!=null&&entry.DisablerCount==0)
                foreach(var pair in state.Overlays)
                {
                    CosmeticPacker.Input input;
                    if(Contains(handlers,pair.Key)&&TryInput(entry,pair.Key,out input)&&ReferenceEquals(input.Arguments,pair.Value.Packed))
                        entry.Part.ChangeArguments(new[]{pair.Key},entry.Arguments);
                }
            state.Overlays.Clear();
        }
        internal void Clear()
        {
            if(manager!=null)
            {
                try{ReadEntries(manager,current);}catch(Exception){current.Clear();}
                ICustomizationHandler[] handlers;
                try{handlers=ReadHandlers(manager);}catch(Exception){handlers=NoHandlers;}
                foreach(var pair in owned)
                    try{Release(pair.Key,pair.Value,current.Contains(pair.Key),handlers);}catch(Exception){ }
            }
            owned.Clear();current.Clear();stale.Clear();manager=null;warned=false;
        }
        public void Dispose(){if(disposed)return;Clear();disposed=true;}
    }
}
