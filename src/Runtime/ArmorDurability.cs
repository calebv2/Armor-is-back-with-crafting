using System;
using System.Collections.Generic;
using System.Reflection;
using Alta.Networking;
using HarmonyLib;
using UnityEngine;

namespace PlayerArmorMod
{
    // The real native module provides its built-in save topic and durability
    // behavior. Only its destructive zero-integrity outcome is replaced for our
    // tagged armor, so broken pieces remain available to repair/remove/trade.
    internal static class ArmorDurability
    {
        static readonly HashSet<ArmorPickup> active=new HashSet<ArmorPickup>();
        static readonly FieldInfo integrity=ArmorNative.Field(typeof(DurabilityModule),"integrity");
        static readonly FieldInfo baseDurability=ArmorNative.Field(typeof(DurabilityModule),"baseDurability");
        static readonly FieldInfo currentDurability=ArmorNative.Field(typeof(DurabilityModule),"currentDurability");
        static readonly MethodInfo changed=AccessTools.Method(typeof(DurabilityModule),"OnDurabilityChanged");
        static bool installed;
        internal static event Action<ArmorPickup> ConditionChanged;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if(installed)return;installed=true;
            ArmorMaterialState.Install(harmony);
            harmony.Patch(AccessTools.Method(typeof(DurabilityModule),"CheckBreak"),prefix:new HarmonyMethod(typeof(ArmorDurability),"CheckBreakPrefix"));
            harmony.Patch(AccessTools.Method(typeof(DurabilityModule),"AffectDurability"),prefix:new HarmonyMethod(typeof(ArmorDurability),"AffectPrefix"),postfix:new HarmonyMethod(typeof(ArmorDurability),"AffectPostfix"));
            ArmorSettings.Changed+=ConfigurationChanged;
            ArmorDurabilityRepairShop.Install(harmony);
        }
        internal static void AddNative(GameObject root,ArmorPickup marker)
        {
            var settings=ScriptableObject.CreateInstance<DurabilitySettings>();settings.name=root.name+" durability";
            var piece=ArmorSettings.GetPiece(marker.Style,(ArmorSlot)marker.Slot);
            ArmorNative.Set(settings,"isChanceBasedDurability",false);ArmorNative.Set(settings,"durability",piece.maximumDurability);ArmorNative.Set(settings,"damageConversion",1f);
            var module=root.AddComponent<DurabilityModule>();
            ArmorNative.Set(module,"durabilitySettings",settings);ArmorNative.Set(module,"stageVisuals",new DurabilityModule.DurabilityVisual[0]);
            ArmorNative.Set(module,"stateChangeEffect",new PickupEvent());module.brokeOnServer=new AltaEvent();
            ArmorNative.Set(module,"canDockWhenDamaged",true);ArmorNative.Set(module,"integrity",1f);
            ArmorNative.Set(module,"baseDurability",piece.maximumDurability);ArmorNative.Set(module,"currentDurability",piece.maximumDurability);
            ArmorNative.Set(root.GetComponent<Pickup>(),"durabilityModule",module);
        }
        internal static void Register(ArmorPickup marker)
        {if(marker!=null){active.Add(marker);if(NetworkSceneManager.IsServer)Normalize(marker);}}
        internal static void Unregister(ArmorPickup marker){active.Remove(marker);}
        static ArmorPickup Marker(DurabilityModule module)
        {var marker=module==null?null:module.GetComponent<ArmorPickup>();return marker!=null&&marker.GetComponent<Pickup>()?.DurabilityModule==module?marker:null;}
        internal static float Condition(ArmorPickup marker)
        {var module=marker==null?null:marker.GetComponent<DurabilityModule>();return module==null?0:ArmorDurabilityPolicy.Condition(module.Integrity);}
        internal static float EffectiveMaximum(ArmorPickup marker)
        {
            if(marker==null||!ArmorCatalog.IsValidStyle(marker.Style)||!ArmorCatalog.IsValidSlot((ArmorSlot)marker.Slot))return 1f;
            return ArmorMaterialState.EffectiveMaximum(marker,ArmorSettings.GetPiece(marker.Style,(ArmorSlot)marker.Slot).maximumDurability);
        }
        internal static void RefreshMaximum(ArmorPickup marker)
        {Normalize(marker);}
        static DurabilityModule Normalize(ArmorPickup marker)
        {
            if(marker==null||!ArmorCatalog.IsValidStyle(marker.Style)||!ArmorCatalog.IsValidSlot((ArmorSlot)marker.Slot))return null;
            var module=marker.GetComponent<DurabilityModule>();if(module==null)return null;
            float value=ArmorDurabilityPolicy.Condition(module.Integrity),maximum=EffectiveMaximum(marker);
            integrity.SetValue(module,value);baseDurability.SetValue(module,maximum);currentDurability.SetValue(module,maximum*value);return module;
        }
        static void ConfigurationChanged()
        {if(NetworkSceneManager.IsServer)foreach(var marker in new List<ArmorPickup>(active))if(marker!=null)Normalize(marker);}
        static bool CheckBreakPrefix(DurabilityModule __instance,ref bool __result)
        {
            var marker=Marker(__instance);if(marker==null)return true;
            // Do not call Pickup.Break, invoke its break event, or destroy docks.
            // A saved zero remains zero until a real repair/replacement occurs.
            float value=ArmorDurabilityPolicy.Condition(__instance.Integrity);
            if(value>0)return true;integrity.SetValue(__instance,0f);currentDurability.SetValue(__instance,0f);__result=true;return false;
        }
        static bool AffectPrefix(DurabilityModule __instance,ref float damage,out float __state)
        {
            __state=-1;var marker=Marker(__instance);if(marker==null)return true;
            if(!NetworkSceneManager.IsServer||!ArmorSettings.Current.durabilityEnabled)return false;
            Normalize(marker);__state=Condition(marker);
            damage=ArmorDurabilityPolicy.Wear(__state,EffectiveMaximum(marker),damage);
            return damage>0;
        }
        static void AffectPostfix(DurabilityModule __instance,float __state)
        {
            if(__state<0||!NetworkSceneManager.IsServer)return;var marker=Marker(__instance);if(marker==null)return;
            Normalize(marker);if(Math.Abs(Condition(marker)-__state)>.000001f)Notify(marker);
        }
        static void Notify(ArmorPickup marker){var callback=ConditionChanged;if(callback!=null)callback(marker);}
        internal static void Wear(ArmorPickup marker,float damage)
        {if(NetworkSceneManager.IsServer&&ArmorSettings.Current.durabilityEnabled){var module=Normalize(marker);if(module!=null)module.AffectDurability(damage);}}
        internal static bool Repair(Pickup pickup)
        {
            if(!NetworkSceneManager.IsServer||pickup==null)return false;var marker=pickup.GetComponent<ArmorPickup>();var module=Normalize(marker);if(module==null)return false;
            integrity.SetValue(module,1f);Normalize(marker);changed.Invoke(module,new object[]{false});Notify(marker);return true;
        }
        // Exact condition is delivered in authenticated armor snapshots; native
        // DurabilitySync alone transmits stages, not this normalized value.
        internal static void ApplyClientCondition(ArmorPickup marker,float value)
        {if(!NetworkSceneManager.IsServer&&marker!=null){var module=marker.GetComponent<DurabilityModule>();if(module!=null){integrity.SetValue(module,ArmorDurabilityPolicy.Condition(value));Normalize(marker);}}}
        internal static void Dispose(){ArmorSettings.Changed-=ConfigurationChanged;active.Clear();ConditionChanged=null;installed=false;ArmorDurabilityRepairShop.Dispose();ArmorMaterialState.Dispose();}
    }
}
