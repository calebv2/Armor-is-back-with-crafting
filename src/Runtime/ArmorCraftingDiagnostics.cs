using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Alta.Crafting;
using Alta.Networking;
using HarmonyLib;
using UnityEngine;

namespace PlayerArmorMod
{
    // Observe only our projects. These hooks never replace native selection,
    // forging checks, trigger membership, release handling or consumption.
    internal static class ArmorCraftingDiagnostics
    {
        sealed class Sample {internal string State;internal float Next,Last;}
        static readonly Dictionary<ulong,Sample> samples=new Dictionary<ulong,Sample>();
        static readonly FieldInfo project=ArmorNative.Field(typeof(GenericCraftingStation),"currentProject"),tracked=ArmorNative.Field(typeof(GenericCraftingStation),"trackedPickups");
        static readonly FieldInfo definition=ArmorNative.Field(typeof(GenericCraftingPiece),"definition"),gates=ArmorNative.Field(typeof(GenericCraftingComponent),"affectedBy");
        static readonly FieldInfo best=ArmorNative.Field(typeof(GenericCraftingComponent),"bestPieceKey"),center=ArmorNative.Field(typeof(GenericCraftingComponent),"centrePointOffset"),cached=ArmorNative.Field(typeof(GenericCraftingComponent),"cachedTransform");
        static float nextPoll;static bool installed;
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if(installed)return;installed=true;
            harmony.Patch(AccessTools.Method(typeof(GenericCraftingStation),"LoadRecipe"),postfix:new HarmonyMethod(typeof(ArmorCraftingDiagnostics),"Loaded"));
            harmony.Patch(AccessTools.Method(typeof(GenericCraftingStation),"TriggerEnter"),postfix:new HarmonyMethod(typeof(ArmorCraftingDiagnostics),"Entered"));
            harmony.Patch(AccessTools.Method(typeof(GenericCraftingStation),"ComponentReleased"),postfix:new HarmonyMethod(typeof(ArmorCraftingDiagnostics),"Released"));
            var track=typeof(GenericCraftingComponent).GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Single(m=>m.Name.EndsWith(".Track",StringComparison.Ordinal));
            harmony.Patch(track,postfix:new HarmonyMethod(typeof(ArmorCraftingDiagnostics),"Tracked"));
        }
        static IProject Owned(GenericCraftingStation station)
        {if(!installed||station==null)return null;var value=project.GetValue(station) as IProject;return value!=null&&ArmorCrafting.OwnsRecipe(value.Recipe)?value:null;}
        static string Number(float value){return value.ToString("0.00",CultureInfo.InvariantCulture);}
        static void Loaded(GenericCraftingStation __instance){try{ObserveProject(__instance,Time.unscaledTime);}catch(Exception){}}
        static void Entered(GenericCraftingStation __instance,Collider other)
        {try{if(Owned(__instance)!=null&&other!=null&&other.attachedRigidbody!=null)Observe(__instance,other.attachedRigidbody.GetComponent<GenericCraftingComponent>(),"trigger-enter",Time.unscaledTime);}catch(Exception){}}
        static void Released(GenericCraftingStation __instance,Interactable interactable)
        {try{if(Owned(__instance)!=null&&interactable!=null)Observe(__instance,interactable.GetComponent<GenericCraftingComponent>(),"release-request",Time.unscaledTime);}catch(Exception){}}
        static void Tracked(GenericCraftingComponent __instance,GenericCraftingStation station)
        {try{Observe(station,__instance,"native-track",Time.unscaledTime);}catch(Exception){}}
        static void Emit(ulong key,string state,float now,bool repeat)
        {
            Sample sample;if(!samples.TryGetValue(key,out sample))
            {if(samples.Count>=256)samples.Remove(samples.OrderBy(pair=>pair.Value.Last).First().Key);samples.Add(key,sample=new Sample());}
            if(now<sample.Next)return;
            sample.Next=now+2;
            if(state==sample.State&&(!repeat||now-sample.Last<5))return;
            sample.State=state;sample.Last=now;
            Core.Log("PLAYER_ARMOR_CRAFT_INPUT role="+(NetworkSceneManager.IsServer?"server":"client")+" "+state);
        }
        static void ObserveProject(GenericCraftingStation station,float now)
        {
            var value=Owned(station);if(value==null)return;
            var pieces=value.MappedPieces.Values;int volumes=station.GetComponentsInChildren<CapsuleCollider>(true).Count(c=>c.isTrigger&&c.enabled&&c.gameObject.activeInHierarchy);
            Emit((ulong)station.Entity.Identifier<<32,"stage=project station="+station.Entity.Identifier+" recipe="+value.Recipe.Hash+" crafting="+ArmorSettings.Current.craftingEnabled+" pieces="+pieces.Count+" enabled="+pieces.Count(p=>p.IsEnabled)+" active="+station.gameObject.activeInHierarchy+" nativeCapsules="+volumes,now,false);
        }
        static void Observe(GenericCraftingStation station,GenericCraftingComponent component,string stage,float now)
        {
            var value=Owned(station);if(value==null||component==null||component.Entity==null||component.Entity.Prefab==null)return;
            ulong key=((ulong)station.Entity.Identifier<<32)|component.Entity.Identifier;
            // Fast throttle before reflection, material lookup and piece scans.
            Sample sample;if(samples.TryGetValue(key,out sample)&&now<sample.Next)return;
            uint source=component.Entity.Prefab.Hash;var origin=(Transform)cached.GetValue(component);if(origin==null)return;
            var position=origin.TransformPoint((Vector3)center.GetValue(component));
            int matching=0,empty=0;float minimum=float.PositiveInfinity;
            foreach(var piece in value.MappedPieces.Values)
            {
                var data=definition.GetValue(piece) as GenericCraftingPieceDefinition;
                if(data==null||data.source==null||data.source.Prefab==null||data.source.Prefab.Hash!=source)continue;
                matching++;if(!piece.IsEnabled||piece.IsFilled)continue;empty++;
                minimum=Math.Min(minimum,(piece.CenterPosition-position).magnitude);
            }
            var checks=gates.GetValue(component) as IAffectCrafting[];bool active=checks!=null&&checks.All(g=>g!=null&&g.IsActive());
            var membership=tracked.GetValue(station) as IDictionary;bool isTracked=membership!=null&&component.Pickup!=null&&component.Pickup.Rigidbody!=null&&membership.Contains(component.Pickup.Rigidbody);
            var material=component.Material;string name=material==null?"none":material.name.Replace(' ','_');
            Emit(key,"stage="+stage+" station="+station.Entity.Identifier+" recipe="+value.Recipe.Hash+" pickup="+component.Entity.Identifier+" source="+source+" material="+name+" crafting="+ArmorSettings.Current.craftingEnabled+" active="+active+" gates="+(checks==null?-1:checks.Length)+" matching="+matching+" enabledEmpty="+empty+" distance="+(float.IsInfinity(minimum)?"none":Number(minimum))+" best="+best.GetValue(component)+" tracked="+isTracked,now,true);
        }
        internal static void Update()
        {
            float now=Time.unscaledTime;if(!installed||now<nextPoll)return;nextPoll=now+1;
            try
            {
                var stations=UnityEngine.Object.FindObjectsOfType<GenericCraftingStation>().Where(s=>Owned(s)!=null).ToArray();if(stations.Length==0)return;
                foreach(var station in stations)ObserveProject(station,now);
                // A hand scan can see a held ingredient even if the native deck
                // never receives TriggerEnter. No world-item or physics scan.
                foreach(var hand in UnityEngine.Object.FindObjectsOfType<Interactor>())
                {
                    var pickup=hand.InteractingWith as Pickup;if(pickup==null)continue;
                    var component=pickup.GetComponent<GenericCraftingComponent>();if(component==null)continue;
                    foreach(var station in stations)if((station.PiecesParent.position-pickup.transform.position).sqrMagnitude<=1.5625f)Observe(station,component,"near-held",now);
                }
            }
            catch(Exception){} // Diagnostics cannot interrupt gameplay.
        }
        internal static void Dispose(){samples.Clear();installed=false;nextPoll=0;}
    }
}
