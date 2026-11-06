using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alta.Character;
using Alta.Inventory;
using Alta.Networking;
using ATT.Saving;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PlayerArmorMod
{
    // One new registered prefab, rather than modifying the player's baked list
    // of embedded entities. Native player saves include this Child prefab.
    internal static class ArmorHarnessTemplate
    {
        internal static GameObject Build(GameObject staging,Item[] armorItems)
        {
            if(staging==null||staging.activeInHierarchy)throw new ArgumentException("Armor harness staging must be inactive.");
            if(armorItems==null||armorItems.Length!=ArmorCatalog.SlotCount||armorItems.Any(i=>i==null))
                throw new ArgumentException("All seven registered armor Items are required.");
            PickupDock donor=FindDonor();
            if(donor==null)throw new InvalidOperationException("A native player back dock with one network entity was not found.");
            var root=new GameObject(ArmorCatalog.HarnessName);root.transform.SetParent(staging.transform,false);
            var entity=root.AddComponent<NetworkEntity>();
            var prefab=root.AddComponent<NetworkPrefab>();Set(prefab,"entity",entity);prefab.SaveMode=SaveMode.Child;prefab.DisableSpawnThroughCommands();
            var harness=root.AddComponent<ArmorHarness>();Set(harness,"entity",entity);
            harness.Docks=new PickupDock[ArmorCatalog.SlotCount];harness.AllowedItems=(Item[])armorItems.Clone();
            for(int i=0;i<ArmorCatalog.SlotCount;i++)
            {
                var go=Object.Instantiate(donor.gameObject,root.transform,false);
                go.name="Armor connection - "+ArmorCatalog.Pieces[i].Slot;
                go.transform.localPosition=Vector3.zero;go.transform.localRotation=Quaternion.identity;go.transform.localScale=Vector3.one;
                foreach(var nested in go.GetComponentsInChildren<NetworkPrefab>(true))Object.DestroyImmediate(nested);
                var dock=go.GetComponent<PickupDock>();var dockEntity=go.GetComponent<NetworkEntity>();
                if(dock==null||dockEntity==null)throw new InvalidOperationException("Native back dock cloning lost required components.");
                var settings=Object.Instantiate(donor.Settings);settings.name="Armor dock settings - "+i;
                // Native back slots require size >= 4; our registered armor is
                // size 2. Normalize only this private settings copy. The exact
                // per-slot item whitelist and server transaction checks remain.
                Set(settings,"minimumCraftingSize",-1);Set(settings,"maximumCraftingSize",-1);
                settings.IncludedItems.Clear();settings.IncludedItems.Add(armorItems[i]);settings.IncludedTags.Clear();settings.ExcludedTags.Clear();settings.ScalingTags.Clear();
                Set(settings,"isSnapping",true);Set(settings,"isGrabbable",false);Set(settings,"dockedScaleMultiplier",1f);
                // A dummy take should use the new grip, not an old held position
                // remembered by the native player-back dock donor.
                Set(settings,"haveInteractorMemory",false);
                Set(settings,"isDockingAutomatic",false);Set(settings,"isStackingDisabled",true);Set(settings,"isStackingEnforced",false);
                Set(settings,"isMultiStackingAllowed",false);Set(settings,"maxContainedStacks",1);Set(settings,"isHandDockingAllowed",false);
                Set(settings,"isAllowingSubDocking",false);Set(settings,"allowsCrafting",false);Set(settings,"allowAdditionalDocks",false);
                Set(settings,"allowPhysicalJoints",false);Set(settings,"allowContainerInteraction",false);Set(settings,"isLengthBasedPositioning",false);
                Set(settings,"maintainPhysicalLayers",(LayerMask)0);Set(settings,"hoverOn",AnimationCurve.Linear(0,1,.1f,1));Set(settings,"hoverOff",AnimationCurve.Linear(0,1,.1f,1));
                Set(dock,"settings",settings);Set(dock,"scaler",null);Set(dock,"dockVisual",null);Set(dock,"dockedType",null);Set(dock,"quantityStored",0);
                Set(dock,"hasContentsRange",new List<Collider>());
                if(dock.SnapPoint==null)throw new InvalidOperationException("Native armor dock has no snap point.");
                dock.SnapPoint.localPosition=Vector3.zero;dock.SnapPoint.localRotation=Quaternion.identity;dock.SnapPoint.localScale=Vector3.one;
                var range=dock.PickupRange;
                if(range==null)throw new InvalidOperationException("Native armor dock has no pickup range.");
                if(range.transform!=go.transform)
                {
                    range.transform.SetParent(go.transform,false);range.transform.localPosition=Vector3.zero;
                    range.transform.localRotation=Quaternion.identity;range.transform.localScale=Vector3.one;
                }
                foreach(var collider in go.GetComponentsInChildren<Collider>(true))if(collider!=range)collider.enabled=false;
                range.isTrigger=true;range.enabled=false;
                var sphere=range as SphereCollider;if(sphere!=null){sphere.center=Vector3.zero;sphere.radius=.19f;}
                var box=range as BoxCollider;if(box!=null){box.center=Vector3.zero;box.size=Vector3.one*.34f;}
                var capsule=range as CapsuleCollider;if(capsule!=null){capsule.center=Vector3.zero;capsule.radius=.17f;capsule.height=.34f;}
                foreach(var renderer in go.GetComponentsInChildren<Renderer>(true))Object.DestroyImmediate(renderer);
                foreach(var visual in go.GetComponentsInChildren<DockVisual>(true))Object.DestroyImmediate(visual);
                foreach(var scaler in go.GetComponentsInChildren<NearbyPlayerScaler>(true))Object.DestroyImmediate(scaler);
                foreach(var rigidbody in go.GetComponentsInChildren<Rigidbody>(true)){rigidbody.isKinematic=true;rigidbody.useGravity=false;}
                // These embedded entities retain inventory/save identity only.
                dock.IsInteractable=false;dock.Channels=InteractionChannel.CharacterEquipment;harness.Docks[i]=dock;
                Set(dockEntity,"hash",i+1);prefab.Embedded.Add(dockEntity);
            }
            root.SetActive(true);
            return root;
        }
        static PickupDock FindDonor()
        {
            foreach(var prefab in PrefabManager.AllPrefabs)
            {
                if(prefab==null)continue;var character=prefab.GetComponent<PlayerCharacter>();
                if(character==null||character.BackDocks==null)continue;
                foreach(var dock in character.BackDocks.Components)
                    if(dock!=null&&dock.Settings!=null&&dock.SnapPoint!=null&&dock.PickupRange!=null
                        &&dock.GetComponent<NetworkEntity>()!=null&&dock.GetComponentsInChildren<NetworkEntity>(true).Length==1)return dock;
            }
            return null;
        }
        static void Set(object target,string name,object value)
        {
            for(Type type=target.GetType();type!=null;type=type.BaseType)
            {
                var field=type.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
                if(field!=null){field.SetValue(target,value);return;}
            }
            throw new MissingFieldException(target.GetType().FullName,name);
        }
    }
}
