using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PlayerArmorMod
{
    // Optional admission adapter only. The existing shop still owns quotes,
    // payment, custody, timers, and its normal native-module Restore operation.
    internal static class ArmorDurabilityRepairShop
    {
        static HarmonyLib.Harmony harmony;static float nextProbe;static bool installed,reportedMissing;
        internal static void Install(HarmonyLib.Harmony value)
        {
            if(!Core.IsServer)return;
            harmony=value;Tick();
        }
        internal static void Tick()
        {
            if(!Core.IsServer||harmony==null||installed||Time.unscaledTime<nextProbe)return;nextProbe=Time.unscaledTime+30f;
            foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type=assembly.GetType("RepairShopMod.RepairItems",false);if(type==null)continue;
                var target=type.GetMethod("IsLooseHead",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(Pickup)},null);
                var quote=type.GetMethod("Quote",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(Pickup)},null);
                var restore=type.GetMethod("Restore",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(Pickup)},null);
                if(target==null||target.ReturnType!=typeof(bool)||quote==null||quote.ReturnType.FullName!="RepairShopMod.ServiceQuote"||restore==null)
                {Core.Error("PLAYER_ARMOR_REPAIRSHOP_UNSUPPORTED optional repair adapter not installed; native armor retained");installed=true;return;}
                harmony.Patch(target,postfix:new HarmonyMethod(typeof(ArmorDurabilityRepairShop),"AdmitArmor"));installed=true;
                Core.Log("PLAYER_ARMOR_REPAIRSHOP_READY registered armor accepted by existing shop; native payment and repair flow retained");return;
            }
            if(!reportedMissing){reportedMissing=true;Core.Log("PLAYER_ARMOR_REPAIRSHOP_OPTIONAL_NOT_FOUND armor repair requires the compatible Repair Shop mod; checking again for late load");}
        }
        internal static bool IsRegisteredArmor(Pickup pickup)
        {
            var assets=Core.Instance==null?null:Core.Instance.Assets;
            if(assets==null||!assets.Ready||pickup==null||pickup.Entity==null||pickup.Entity.Prefab==null||pickup.Item==null||pickup.DurabilityModule==null)return false;
            var marker=pickup.GetComponent<ArmorPickup>();
            if(marker==null||!ArmorCatalog.IsValidStyle(marker.Style)||!ArmorCatalog.IsValidSlot((ArmorSlot)marker.Slot)||marker.GetComponent<DurabilityModule>()!=pickup.DurabilityModule)return false;
            var expected=assets.PiecesFor(marker.Style)[marker.Slot];
            return expected!=null&&pickup.Entity.Prefab.Hash==expected.Hash&&pickup.Item.Hash==expected.Hash;
        }
        static void AdmitArmor(Pickup pickup,ref bool __result)
        {if(!__result&&NetworkSceneManager.IsServer)__result=IsRegisteredArmor(pickup);}
        internal static void Dispose()
        {
            harmony=null;installed=false;reportedMissing=false;nextProbe=0;
        }
    }
}
