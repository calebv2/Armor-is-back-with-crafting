using System;
using System.Collections.Generic;
using Alta.Character;
using Alta.Customization;
using Alta.PlayerStates;
using UnityEngine;
using EquipmentSlot=PlayerArmorMod.Original.EquipmentSlot;
using EquipmentPartPickup=PlayerArmorMod.Original.EquipmentPartPickup;

namespace PlayerArmorMod
{
    // EquipmentState integration adapter. The native quick wheel still opens
    // EquipmentState; this ports its original slot subscription/release flow.
    // There is no replacement wheel and no global hand/network/IK mode changes.
    internal sealed class ArmorDummyInteraction
    {
        internal const float CaptureRadius=.24f;
        internal const float TakeRadius=.13f;
        const float Lifetime=1f;
        readonly Func<string,bool> send;
        readonly Func<Transform,int,uint,Vector3,float?> surfaceDistance;
        readonly EquipmentSlot[] slots=new EquipmentSlot[ArmorCatalog.SlotCount];
        readonly EquipmentPartPickup[] held=new EquipmentPartPickup[2];
        readonly List<Pending> pending=new List<Pending>();
        readonly bool[] wasPressed=new bool[2];
        readonly Controller[] hands=new Controller[2];
        readonly Dictionary<int,uint> wornIds=new Dictionary<int,uint>();
        EquipmentCustomizationRoom room;
        Transform root;
        PlayerCharacter owner;
        string session;
        bool dummy;
        byte mask;
        float now,nextResolve;
        sealed class Pending{internal EquipmentPartPickup Item;internal float Expires,Next;internal int Attempts;internal bool Dummy;}
        internal Transform GuideTarget {get;private set;}
        internal float GuideDistance {get;private set;}
        internal bool GuideReady {get;private set;}
        internal Vector3 GuideRight {get;private set;}
        internal bool GuideIsDummy {get{return dummy;}}
        internal int GuideSlot {get;private set;}
        internal EquipmentCustomizationRoom DummyRoom => dummy && root != null && root.gameObject.activeInHierarchy ? room : null;
        internal int DummyLayer {get;private set;}
        internal Transform DummyPoint(int slot)
        {
            if (DummyRoom == null) return null;
            Transform point = slots[slot].Point;
            return point != null && point.IsChildOf(root) ? point : null;
        }
        internal ArmorDummyInteraction(Func<string,bool> sender,Func<Transform,int,uint,Vector3,float?> surface=null)
        {
            if(sender==null)throw new ArgumentNullException("sender");send=sender;surfaceDistance=surface;
            for(int i=0;i<slots.Length;i++){slots[i]=new EquipmentSlot((ArmorSlot)i);slots[i].Attach=AttachCharacterPart;}
        }
        internal void SetWornItems(ArmorHarness harness)
        {
            wornIds.Clear();if(harness==null||harness.Docks==null)return;
            for(int i=0;i<harness.Docks.Length;i++)
            {var dock=harness.Docks[i];var p=dock==null?null:dock.DockedPickup;if(p!=null&&p.Entity!=null)wornIds[i]=p.Entity.Identifier;}
        }
        // Mirrors original EquipmentState.OnDeactivate cleanup.
        internal void Reset()
        {
            foreach(var p in held)if(p!=null)p.Dispose();Array.Clear(held,0,held.Length);
            foreach(var p in pending)p.Item.Dispose();pending.Clear();
            foreach(var s in slots){s.Point=null;s.Occupied=false;}
            Array.Clear(wasPressed,0,wasPressed.Length);room=null;root=null;owner=null;session=null;GuideTarget=null;GuideSlot=-1;nextResolve=0;
        }
        internal void Update(PlayerCharacter actual,byte equippedMask,string nonce,float time)
        {
            GuideTarget=null;GuideDistance=float.PositiveInfinity;GuideReady=false;GuideSlot=-1;now=time;
            var local=Player.Current;
            if(actual==null||actual.IsSpirit||local==null||!object.ReferenceEquals(local.PlayerController,actual)||!ArmorProtocol.IsToken(nonce))
            {Reset();return;}
            var equipment=local.StateManager==null?null:local.StateManager.CurrentState as EquipmentState;
            var current=equipment==null?null:equipment.CustomizationRoom;
            bool inDummy=current!=null&&object.ReferenceEquals(current.Player,local)&&current.gameObject.activeInHierarchy
                &&current.Dummy!=null&&current.Dummy.Root!=null&&current.Dummy.Root.gameObject.activeInHierarchy;
            // An active but not ready equipment state must not silently fit to
            // the real body while the native mannequin is being constructed.
            if(equipment!=null&&!inDummy){Reset();return;}
            var newRoot=inDummy?current.Dummy.Root:actual.CustomizationHandler==null?null:actual.CustomizationHandler.RootBone;
            if(newRoot==null||!newRoot.gameObject.activeInHierarchy){Reset();return;}
            if(owner!=actual||root!=newRoot||session!=nonce||dummy!=inDummy)
            {
                Reset();owner=actual;root=newRoot;session=nonce;room=inDummy?current:null;dummy=inDummy;ResolvePoints();
                if (inDummy) DummyLayer = PlayerArmorRigBinding.BodyLayer(room.Dummy);
            }
            mask=equippedMask;
            if(now>=nextResolve){ResolvePoints();nextResolve=now+.25f;}
            hands[0]=owner.LeftController;hands[1]=owner.RightController;
            var armor0=Held(hands[0]);var armor1=Held(hands[1]);
            for(int i=0;i<slots.Length;i++){slots[i].Occupied=(mask&(1<<i))!=0;slots[i].IsDummy=dummy;}
            for(int h=0;h<2;h++)
            {
                var armor=h==0?armor0:armor1;var hand=hands[h];var previous=held[h];
                if(previous!=null&&(armor!=previous.Armor))
                {
                    if(previous.ReleaseDeadline==0)previous.ReleaseDeadline=now+Lifetime;
                    bool otherHolds=previous.Armor==armor0||previous.Armor==armor1;
                    if(otherHolds||now>previous.ReleaseDeadline||!Valid(previous)||!Near(previous)||!HandFree(hand))
                    {previous.Dispose();held[h]=null;}
                    else if(Free(previous))
                    {
                        // Polling is the compatibility fallback for a native
                        // release callback that arrives before its flags clear.
                        previous.ItemLetGoInCustomizationMode(previous.Pickup,hand==null?null:hand.Interactor);
                        previous.Dispose();held[h]=null;
                    }
                }
                if(armor!=null&&armor.Slot>=0&&armor.Slot<slots.Length&&armor.Entity!=null&&armor.Entity.Identifier!=0&&!armor.Entity.IsBeingDestroyed)
                {
                    CancelPending(armor);
                    var candidate=held[h];
                    if(candidate==null||candidate.Armor!=armor)
                    {if(candidate!=null)candidate.Dispose();candidate=new EquipmentPartPickup(armor);held[h]=candidate;candidate.SetToCustomizationMode(true);}
                    var slot=slots[armor.Slot];
                    if(slot.Point==null||!slot.Point.IsChildOf(root)||candidate.Pickup==null||candidate.Pickup.Dock!=null)continue;
                    float distance=Vector3.Distance(armor.transform.position,slot.Point.position);
                    if(distance<GuideDistance){GuideTarget=slot.Point;GuideSlot=armor.Slot;GuideDistance=distance;GuideReady=!slot.Occupied&&distance<=CaptureRadius;GuideRight=dummy?room.transform.right:owner.transform.right;}
                    if(!slot.Occupied&&distance<=CaptureRadius)slot.CheckEnteredObject(candidate);else slot.CheckExitedObject(candidate);
                    candidate.ReleaseDeadline=0;
                }
                bool pressed=hand!=null&&hand.ActionsManager!=null&&hand.ActionsManager[ControllerActionType.Interact];
                if(dummy&&armor==null&&pressed&&!wasPressed[h]&&HandFree(hand))RemoveFromSlot(hand,h==1);
                wasPressed[h]=pressed;
            }
            for(int i=pending.Count-1;i>=0;i--)
            {
                var p=pending[i];int slot=(int)p.Item.Category;
                if(now>p.Expires||p.Attempts>=3||!Free(p.Item)||(mask&(1<<slot))!=0||p.Item.Armor==armor0||p.Item.Armor==armor1
                    ||slots[slot].Point==null||!slots[slot].Point.IsChildOf(root)||p.Dummy!=dummy)
                {pending.RemoveAt(i);continue;}
                if(now<p.Next)continue;
                send(p.Dummy?ArmorProtocol.EncodeEquip(session,p.Item.Category,p.Item.Armor.Entity.Identifier):ArmorProtocol.EncodeWear(session,p.Item.Category,p.Item.Armor.Entity.Identifier));
                p.Attempts++;p.Next=now+.3f;
            }
        }
        void AttachCharacterPart(EquipmentPartPickup item)
        {
            if(item.Submitted||!Valid(item)||!Free(item)||!Near(item)||(mask&(1<<(int)item.Category))!=0)return;
            // Native event must never bypass the matching point/release checks.
            if(Held(hands[0])==item.Armor||Held(hands[1])==item.Armor)return;
            item.Submitted=true;pending.Add(new Pending{Item=item,Expires=now+Lifetime,Next=now,Dummy=dummy});
        }
        void RemoveFromSlot(Controller hand,bool rightHand)
        {
            if(hand.Interactor.InteractionPoint==null)return;
            int best=-1,fallback=-1;float distance=TakeRadius,fallbackDistance=TakeRadius;
            Vector3 p=hand.Interactor.InteractionPoint.position;
            for(int i=0;i<slots.Length;i++)
            {
                uint id;var slot=slots[i];if(!slot.Occupied||!wornIds.TryGetValue(i,out id))continue;
                float? d=surfaceDistance==null?null:surfaceDistance(root,i,id,p);
                if(d.HasValue)
                {
                    if(d.Value<distance){distance=d.Value;best=i;}
                }
                else if(slot.Point!=null)
                {
                    float boneDistance=Vector3.Distance(p,slot.Point.position);
                    if(boneDistance<fallbackDistance){fallbackDistance=boneDistance;fallback=i;}
                }
            }
            if(best<0)best=fallback;
            if(best>=0)send(ArmorProtocol.EncodeTake(session,(ArmorSlot)best,wornIds[best],rightHand));
        }
        void ResolvePoints()
        {
            if(root==null)return;var transforms=root.GetComponentsInChildren<Transform>(true);
            foreach(var slot in slots)
            {
                Transform found=null;foreach(var t in transforms)if(t.name==ArmorCatalog.Get(slot.Category).BoneName&&!OriginalArmorArt.IsSourceBone(t)){found=t;break;}
                if(found!=slot.Point)
                {
                    foreach(var p in held)if(p!=null&&p.Slot==slot)p.Slot=null;
                    pending.RemoveAll(p=>p.Item.Category==slot.Category);slot.Point=found;
                }
            }
        }
        bool Near(EquipmentPartPickup item)
        {return item.Slot!=null&&item.Slot.Point!=null&&item.Slot.Point.IsChildOf(root)&&Vector3.Distance(item.Armor.transform.position,item.Slot.Point.position)<=CaptureRadius+.02f;}
        static bool Valid(EquipmentPartPickup item)
        {return item!=null&&item.Armor!=null&&item.Pickup!=null&&item.Armor.Entity!=null&&!item.Armor.Entity.IsBeingDestroyed&&item.Pickup.Dock==null;}
        static bool Free(EquipmentPartPickup item){return Valid(item)&&!item.Pickup.IsInteractedWith&&item.Pickup.Interactors.Count==0;}
        static bool HandFree(Controller hand){return hand!=null&&hand.Interactor!=null&&hand.Interactor.InteractingWith==null;}
        internal static ArmorPickup Held(Controller hand)
        {var p=hand==null||hand.Interactor==null?null:hand.Interactor.InteractingWith as Pickup;return p==null?null:p.GetComponent<ArmorPickup>();}
        void CancelPending(ArmorPickup armor){pending.RemoveAll(p=>p.Item.Armor==armor);}
    }
}
