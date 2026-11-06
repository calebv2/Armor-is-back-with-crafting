using System;
using Alta.Character;
using Alta.Inventory;
using HarmonyLib;
using UnityEngine;

namespace PlayerArmorMod
{
    // Native Interactor collects candidates before calling InteractionFilter.
    // Exclude unusable armor targets before collection, without changing the
    // climbing test, ordinary world interactables, or native inventory methods.
    internal static class ArmorHandInteraction
    {
        static bool active;
        static bool reportedStorage;
        static Action<string> diagnostic;

        internal static void Install(HarmonyLib.Harmony harmony,Action<string> logger=null)
        {
            var select=AccessTools.Method(typeof(Interactor),"SelectBest",new[]{typeof(int),typeof(Interactable).MakeByRefType()});
            var add=AccessTools.Method(typeof(Interactor),"AddOverlap",new[]{typeof(Collider)});
            var buffer=AccessTools.Field(typeof(Interactor),"overlap");
            if(select==null||add==null||buffer==null||buffer.FieldType!=typeof(Collider[]))
                throw new MissingMemberException("Native hand overlap methods changed; armor hand guard cannot be installed.");
            harmony.Patch(select,prefix:new HarmonyMethod(typeof(ArmorHandInteraction),"BeforeSelect"));
            harmony.Patch(add,prefix:new HarmonyMethod(typeof(ArmorHandInteraction),"BeforeAdd"));
            diagnostic=logger;reportedStorage=false;active=true;
        }
        internal static void ResetDiagnostics(){reportedStorage=false;}
        internal static void Dispose(){active=false;diagnostic=null;reportedStorage=false;}

        static void BeforeSelect(Interactor __instance,ref int overlapCount,Collider[] ___overlap)
        {if(active)overlapCount=FilterOverlaps(__instance,___overlap,overlapCount);}
        static bool BeforeAdd(Interactor __instance,Collider collider)
        {return !active||AllowOverlap(__instance,collider);}

        internal static int FilterOverlaps(Interactor hand,Collider[] buffer,int count)
        {
            if(hand==null||!hand.IsLocal||buffer==null)return count;
            int length=Math.Min(Math.Max(count,0),buffer.Length),kept=0;
            for(int index=0;index<length;index++)
                if(AllowOverlap(hand,buffer[index]))buffer[kept++]=buffer[index];
            for(int index=kept;index<length;index++)buffer[index]=null;
            return kept;
        }
        internal static bool AllowOverlap(Interactor hand,Collider collider)
        {
            if(hand==null||!hand.IsLocal||collider==null)return true;
            var dock=collider.GetComponent<PickupDock>();
            if(dock==null)
            {
                // Also cover a worn item's own collider if native docking or
                // another component has made it queryable. Loose items retain
                // the normal native interaction path.
                var pickup=collider.GetComponent<Pickup>();
                if(pickup==null&&collider.attachedRigidbody!=null)
                    pickup=collider.attachedRigidbody.GetComponent<Pickup>();
                if(pickup!=null)dock=pickup.Dock;
            }
            if(dock==null)return true;
            var harness=dock.GetComponentInParent<ArmorHarness>();
            if(harness==null)return true;
            try{return AllowArmor(hand,dock,harness);}
            catch(Exception){return false;} // A streaming armor target is unavailable; world interactions continue.
        }
        static bool AllowArmor(Interactor hand,PickupDock dock,ArmorHarness harness)
        {
            // Original-slot equipment has no grabbable worn storage sockets.
            // This compatibility guard also covers old streamed collider states.
            if(!reportedStorage&&diagnostic!=null)
            {reportedStorage=true;try{diagnostic("PLAYER_ARMOR_STORAGE_OVERLAP_FILTERED noninteractive saved equipment");}catch(Exception){}}
            return false;
        }

        internal static void UpdateHarness(ArmorHarness harness)
        {if(harness!=null)harness.DisableHandTargets();}
    }
}
