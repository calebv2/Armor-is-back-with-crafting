using System;
using System.Collections.Generic;
using Alta.Character;
using Alta.Inventory;
using Alta.Networking;
using ATT.Saving;
using UnityEngine;

namespace PlayerArmorMod
{
    // Persistence adapter for the original EquipmentSlot flow. The same seven
    // embedded entities preserve saved items, but are NEVER hand targets.
    public sealed class ArmorHarness : NetworkEntityBehaviour
    {
        public PickupDock[] Docks;
        public Item[] AllowedItems;
        public Item[] LeatherItems;
        public Item[] WoodlandItems;
        public Item[] HebiosItems;
        public Item[] MushroomItems;
        public Item[] WyrmItems;
        public Item[] CrystalWyrmItems;
        public Item[] ForgeguardItems;
        public Item[] StandaloneHelmetItems;
        internal Item ItemFor(ArmorStyle style,ArmorSlot slot)
        {
            if(!ArmorCatalog.IsValidStyle(style,slot))return null;
            bool standalone=ArmorCatalog.IsStandaloneHelmet(style);
            var items=standalone?StandaloneHelmetItems:ItemsFor(style);
            int index=standalone?ArmorCatalog.HelmetIndex(style):(int)slot;
            return items!=null&&index<items.Length?items[index]:null;
        }
        public Item[] ItemsFor(ArmorStyle style)
        {
            switch(style)
            {
                case ArmorStyle.Metal:return AllowedItems;
                case ArmorStyle.Leather:return LeatherItems;
                case ArmorStyle.Woodland:return WoodlandItems;
                case ArmorStyle.Hebios:return HebiosItems;
                case ArmorStyle.Mushroom:return MushroomItems;
                case ArmorStyle.Wyrm:return WyrmItems;
                case ArmorStyle.CrystalWyrm:return CrystalWyrmItems;
                case ArmorStyle.Forgeguard:return ForgeguardItems;
                default:return null;
            }
        }
        public uint StyleBits
        {
            get
            {
                uint result=0;if(Docks==null)return result;
                for(int i=0;i<Math.Min(Docks.Length,ArmorCatalog.SlotCount);i++)
                {
                    var dock=Docks[i];var pickup=dock==null||dock.QuantityStored<=0||!dock.HasDockedPickup?null:dock.DockedPickup;
                    if(pickup==null)continue;
                    var style=StyleForItem(i,pickup.Item)??ArmorStyle.Metal;
                    result=ArmorCatalog.SetStyle(result,(ArmorSlot)i,style);
                }
                return result;
            }
        }
        public PlayerCharacter Owner {get;private set;}
        public bool BonesReady {get;private set;}
        internal bool SessionActive {get;set;}
        public uint OwnerEntityId {get{return Owner!=null&&Owner.Entity!=null?Owner.Entity.Identifier:ownerHint;}}
        public event Action<ArmorHarness> EquipmentChanged;
        public byte EquippedMask
        {
            get
            {
                byte mask=0;if(Docks==null)return mask;
                for(int i=0;i<Math.Min(Docks.Length,ArmorCatalog.SlotCount);i++)
                    if(Docks[i]!=null&&Docks[i].QuantityStored>0&&Docks[i].HasDockedPickup)mask|=(byte)(1<<i);
                return mask;
            }
        }
        public byte LeatherMask
        {
            get
            {
                byte mask=0;if(Docks==null||LeatherItems==null)return mask;
                for(int i=0;i<Math.Min(Docks.Length,ArmorCatalog.SlotCount);i++)
                {
                    var dock=Docks[i];
                    if(dock!=null&&dock.QuantityStored>0&&dock.HasDockedPickup&&dock.DockedPickup!=null
                        &&MatchesItem(LeatherItems,i,dock.DockedPickup.Item))mask|=(byte)(1<<i);
                }
                return mask;
            }
        }
        readonly Transform[] bones=new Transform[ArmorCatalog.SlotCount];
        readonly List<FilterBinding> filters=new List<FilterBinding>();
        sealed class FilterBinding {internal PickupDock Dock;internal DockFilter Insert;internal InteractionFilter Remove;}
        Transform cachedRoot,transactionAnchor;
        Pickup transactionPickup;
        int transactionSlot=-1;
        bool transactionDummy,initialized;
        uint ownerHint;
        float nextOwnerRetry,nextWarning;
        internal const float CaptureRadius=.24f;

        public override void Initialize(){base.Initialize();InitializeHarness();}
        public override void InitializeAsServer(SpawnData data)
        {base.InitializeAsServer(data);InitializeHarness();if(Entity!=null&&Entity.Prefab!=null)Entity.Prefab.SaveMode=SaveMode.Child;}
        void InitializeHarness()
        {
            if(initialized)return;
            if(Docks==null||AllowedItems==null||Docks.Length!=ArmorCatalog.SlotCount||AllowedItems.Length!=ArmorCatalog.SlotCount)
                throw new InvalidOperationException("Equipment persistence requires seven registered item slots.");
            if(LeatherItems!=null&&LeatherItems.Length!=ArmorCatalog.SlotCount)
                throw new InvalidOperationException("Leather persistence requires seven registered item slots.");
            foreach(var items in new[]{WoodlandItems,HebiosItems,MushroomItems,WyrmItems,CrystalWyrmItems,ForgeguardItems})
                if(items!=null&&items.Length!=ArmorCatalog.SlotCount)throw new InvalidOperationException("Armor family persistence requires seven registered item slots.");
            if(StandaloneHelmetItems!=null&&StandaloneHelmetItems.Length!=ArmorCatalog.StandaloneHelmets.Count)
                throw new InvalidOperationException("Incomplete standalone helmet registration.");
            for(int i=0;i<Docks.Length;i++)
            {
                int slot=i;var dock=Docks[i];if(dock==null||AllowedItems[i]==null)throw new InvalidOperationException("Incomplete armor slot "+i);
                var binding=new FilterBinding{Dock=dock};
                binding.Insert=delegate(Interactor hand,Pickup pickup,ref bool allowed){FilterInsert(slot,hand,pickup,ref allowed);};
                binding.Remove=delegate(Interactor hand,Interactable item,ref bool allowed){allowed=false;};
                dock.DockFilter+=binding.Insert;dock.InteractionFilter+=binding.Remove;dock.OverridePositioning=PositionArmor;
                dock.ItemDocked+=OnItemChanged;dock.ItemUndocked+=OnItemChanged;filters.Add(binding);
            }
            initialized=true;DisableHandTargets();ResolveOwner();
        }
        public void SetOwnerEntityId(uint id){ownerHint=id;if(Owner!=null&&(Owner.Entity==null||Owner.Entity.Identifier!=id))ClearOwner();ResolveOwner();}
        public void SetOwner(PlayerCharacter owner)
        {
            if(owner==null){ClearOwner();return;}
            if(Owner!=owner){Owner=owner;cachedRoot=null;BonesReady=false;Array.Clear(bones,0,bones.Length);SessionActive=false;}
            ownerHint=owner.Entity!=null?owner.Entity.Identifier:0;
            if(NetworkSceneManager.IsServer&&Entity!=null)
            {
                if(Entity.Prefab!=null)Entity.Prefab.SaveMode=SaveMode.Child;
                if(Entity.ChunkingParent!=owner.Entity)Entity.ChunkingParent=owner.Entity;
            }
            if(transform.parent!=owner.transform)transform.SetParent(owner.transform,false);
            transform.localPosition=Vector3.zero;transform.localRotation=Quaternion.identity;transform.localScale=Vector3.one;
            if(Docks!=null)foreach(var dock in Docks)if(dock!=null&&dock.PlayerController!=owner)
            {
                if(owner.IsLocalPlayer)((IInitializeAsLocalPlayer)dock).InitializeAsLocalPlayer(owner);
                else ((IInitializeAsRemotePlayer)dock).InitializeAsRemotePlayer(owner);
            }
            DisableHandTargets();UpdateBoneFrames();
        }
        void ClearOwner(){Owner=null;SessionActive=false;BonesReady=false;cachedRoot=null;Array.Clear(bones,0,bones.Length);}
        void ResolveOwner()
        {
            if(Entity==null||!Entity.IsInitialized)return;
            var parent=Entity.ChunkingParent;
            for(int depth=0;parent!=null&&depth<8;depth++,parent=parent.ChunkingParent)
            {var character=parent.GetComponent<PlayerCharacter>();if(character!=null){SetOwner(character);return;}}
            if(ownerHint!=0&&Entity.Scene!=null)
            {var entity=Entity.Scene.GetEntity(ownerHint);var character=entity==null?null:entity.GetComponent<PlayerCharacter>();if(character!=null)SetOwner(character);}
        }
        void LateUpdate(){Tick();}
        public void Tick()
        {
            if(!initialized||Entity==null||!Entity.IsInitialized||Entity.IsBeingDestroyed)return;
            try
            {
                DisableHandTargets();float now=Time.unscaledTime;
                if(Owner==null||now>=nextOwnerRetry){nextOwnerRetry=now+.5f;ResolveOwner();}
                if(Owner==null){BonesReady=false;return;}UpdateBoneFrames();
            }
            catch(Exception error)
            {if(Time.unscaledTime>=nextWarning){nextWarning=Time.unscaledTime+15;Debug.LogWarning("[PlayerArmor] Equipment persistence deferred: "+error.GetBaseException().Message);}}
        }
        internal void DisableHandTargets()
        {
            if(Docks==null)return;
            foreach(var dock in Docks)if(dock!=null)
            {
                dock.IsInteractable=false;
                if(dock.PickupRange!=null&&dock.PickupRange.enabled)dock.PickupRange.enabled=false;
                dock.transform.localPosition=Vector3.zero;dock.transform.localRotation=Quaternion.identity;dock.transform.localScale=Vector3.one;
            }
        }
        void UpdateBoneFrames()
        {
            BonesReady=false;if(Owner==null||Owner.CustomizationHandler==null)return;
            var root=Owner.CustomizationHandler.RootBone;if(root==null)return;
            bool rebuild=root!=cachedRoot;
            foreach(var bone in bones)if(bone==null||!bone.IsChildOf(root))rebuild=true;
            if(rebuild)
            {
                var transforms=root.GetComponentsInChildren<Transform>(true);cachedRoot=root;
                for(int i=0;i<bones.Length;i++)
                {bones[i]=null;foreach(var bone in transforms)if(bone.name==ArmorCatalog.Pieces[i].BoneName){bones[i]=bone;break;}}
            }
            foreach(var bone in bones)if(bone==null||!Finite(bone.position))return;
            BonesReady=true;
        }
        internal bool TryEquipFromDummy(ArmorSlot slot,Pickup pickup,Transform anchor,out string reason)
        {return AttachCharacterPart(slot,pickup,anchor,true,out reason);}
        internal bool TryEquipToBody(ArmorSlot slot,Pickup pickup,out string reason)
        {return AttachCharacterPart(slot,pickup,null,false,out reason);}
        bool AttachCharacterPart(ArmorSlot slot,Pickup pickup,Transform anchor,bool dummy,out string reason)
        {
            reason="InvalidContext";int index=(int)slot;
            if(!NetworkSceneManager.IsServer||!initialized||!SessionActive||transactionPickup!=null||!ArmorCatalog.IsValidSlot(slot)
                ||Owner==null||Owner.Entity==null||Owner.Entity.IsBeingDestroyed||!Owner.IsAlive||Owner.IsSpirit||!BonesReady
                ||pickup==null||pickup.Entity==null||pickup.Entity.IsBeingDestroyed||!Matches(index,pickup))return false;
            var dock=Docks[index];if(dock==null)return false;
            if(pickup.IsDocked){reason=pickup.Dock==dock?"AlreadyEquipped":"AlreadyDocked";return pickup.Dock==dock;}
            if(dock.HasDockedPickup||dock.QuantityStored>0){reason="Occupied";return false;}
            if(pickup.IsDockingBlocked){reason="DockingBlocked";return false;}
            if(HasHands(pickup)){reason="WaitingForRelease";return false;}
            if(!ReleasedByOwner(pickup)){reason="NotOwned";return false;}
            if(!(dummy?WithinDummyReach(pickup,anchor):WithinBodyReach(index,pickup))){reason="OutsideSlotReach";return false;}
            transactionPickup=pickup;transactionSlot=index;transactionAnchor=anchor;transactionDummy=dummy;
            try
            {
                var result=pickup.DockInto(null,dock,1,true);reason=result.ToString();
                if(result!=PickupDock.AllowedResult.Ok)return false;
                Debug.Log("[PlayerArmor] PLAYER_ARMOR_EQUIPPED slot="+slot+" controller="+OwnerEntityId+" source="+(dummy?"quick-wheel":"body"));return true;
            }
            finally{transactionPickup=null;transactionAnchor=null;transactionSlot=-1;transactionDummy=false;}
        }
        bool WithinBodyReach(int slot,Pickup pickup)
        {return slot>=0&&slot<bones.Length&&bones[slot]!=null&&Finite(pickup.transform.position)&&Vector3.Distance(pickup.transform.position,bones[slot].position)<=CaptureRadius+.10f;}
        bool WithinDummyReach(Pickup pickup,Transform anchor)
        {
            if(pickup==null||anchor==null||Owner==null||Owner.Head==null)return false;
            Vector3 position=pickup.transform.position,origin=anchor.position,feet=Owner.PlayerFeetPosition;
            if(!Finite(position)||!Finite(origin)||!Finite(feet)||!Finite(Owner.Head.position))return false;
            float scale=Mathf.Clamp(Owner.transform.lossyScale.y,.5f,2f);
            if(Vector3.Distance(feet,origin)>3.5f*scale)return false;
            float x=position.x-origin.x,z=position.z-origin.z,y=position.y-origin.y;
            if(x*x+z*z>.95f*.95f*scale*scale||y<-.25f*scale||y>2.5f*scale)return false;
            var l=Owner.LeftController;var r=Owner.RightController;
            return(l!=null&&Finite(l.transform.position)&&Vector3.Distance(position,l.transform.position)<=1.25f*scale)
                ||(r!=null&&Finite(r.transform.position)&&Vector3.Distance(position,r.transform.position)<=1.25f*scale);
        }
        void FilterInsert(int slot,Interactor hand,Pickup pickup,ref bool allowed)
        {
            // Server-authorized native docking replication and saved-item loading
            // also execute this filter on clients. Never require a server-only
            // transaction object on that receiving presentation path.
            if(!NetworkSceneManager.IsServer){allowed=allowed&&pickup!=null&&Matches(slot,pickup);return;}
            if(!allowed||!SessionActive||pickup==null||!Matches(slot,pickup)||transactionPickup!=pickup||transactionSlot!=slot
                ||Owner==null||!Owner.IsAlive||Owner.IsSpirit||HasHands(pickup)||!ReleasedByOwner(pickup)
                ||(Docks[slot].HasDockedPickup&&Docks[slot].DockedPickup!=pickup))
            {allowed=false;return;}
            if(hand!=null&&(hand.Controller==null||hand.Controller.PlayerController!=Owner)){allowed=false;return;}
            allowed=transactionDummy?WithinDummyReach(pickup,transactionAnchor):WithinBodyReach(slot,pickup);
        }
        bool Matches(int slot,Pickup pickup)
        {return pickup!=null&&StyleForItem(slot,pickup.Item).HasValue;}
        ArmorStyle? StyleForItem(int slot,Item item)
        {
            if(slot==(int)ArmorSlot.Head&&StandaloneHelmetItems!=null)
                for(int i=0;i<StandaloneHelmetItems.Length;i++)
                    if(MatchesItem(StandaloneHelmetItems,i,item))return ArmorCatalog.StandaloneHelmets[i].Style;
            if(MatchesItem(ForgeguardItems,slot,item))return ArmorStyle.Forgeguard;
            for(int index=0;index<=(int)ArmorStyle.CrystalWyrm;index++)
            {
                var style=(ArmorStyle)index;
                if(MatchesItem(ItemsFor(style),slot,item))return style;
            }
            return null;
        }
        static bool MatchesItem(Item[] items,int slot,Item item)
        {return items!=null&&slot>=0&&slot<items.Length&&items[slot]!=null&&item!=null&&item.Hash==items[slot].Hash;}
        bool ReleasedByOwner(Pickup pickup)
        {return Owner!=null&&Owner.NetworkPlayer!=null&&Owner.NetworkPlayer.UserInfo!=null&&!HasHands(pickup)
            &&(pickup.LastPlayerInteractor==Owner||pickup.LastInteractorPlayerId==Owner.NetworkPlayer.UserInfo.Identifier);}
        static bool HasHands(Pickup pickup){return pickup.IsInteractedWith||pickup.Interactors.Count>0;}
        static bool Finite(Vector3 p){return !(float.IsNaN(p.x)||float.IsNaN(p.y)||float.IsNaN(p.z)||float.IsInfinity(p.x)||float.IsInfinity(p.y)||float.IsInfinity(p.z));}
        static bool PositionArmor(PickupDock dock,Item item,Pickup pickup,bool scaling,out Vector3 position,out Quaternion rotation,out Vector3 scale)
        {position=Vector3.zero;rotation=Quaternion.identity;scale=Vector3.one;return true;}
        void OnItemChanged(Interactor hand,Pickup pickup,PickupDock dock){var changed=EquipmentChanged;if(changed!=null)changed(this);}
        void OnDestroy()
        {
            foreach(var binding in filters)if(binding.Dock!=null)
            {binding.Dock.DockFilter-=binding.Insert;binding.Dock.InteractionFilter-=binding.Remove;binding.Dock.ItemDocked-=OnItemChanged;binding.Dock.ItemUndocked-=OnItemChanged;}
            filters.Clear();SessionActive=false;
        }
    }
}
