using System;
using System.Reflection;
using Alta.Networking;
using Alta.Pages;
using HarmonyLib;

namespace PlayerArmorMod
{
    // Original paper synchronization installs a native no-grab filter before
    // the first client grip. Our registered recipe originals still use the
    // server's native acquire/release/unlock flow; remove only that one client
    // filter so a player can begin the first grip.
    internal static class ArmorRecipePageInteraction
    {
        static readonly MethodInfo originalGrabFilter=AccessTools.Method(typeof(PaperPageInstance),"OriginalInteractionFilter",
            new[]{typeof(Interactor),typeof(Interactable),typeof(bool).MakeByRefType()});

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if(originalGrabFilter==null)throw new MissingMethodException("Native PaperPageInstance.OriginalInteractionFilter is unavailable");
            var target=AccessTools.Method(typeof(PaperPageInstance),"MarkAsDuplicate",new[]{typeof(bool)});
            if(target==null)throw new MissingMethodException("Native PaperPageInstance.MarkAsDuplicate is unavailable");
            harmony.Patch(target,postfix:new HarmonyMethod(typeof(ArmorRecipePageInteraction),"MarkPostfix"));
        }

        static void MarkPostfix(PaperPageInstance __instance,bool isDuplicate)
        {
            if(isDuplicate||NetworkSceneManager.IsServer||!ArmorCrafting.IsRegisteredRecipePage(__instance))return;
            RemoveNativeGrabFilter(__instance);
        }

        internal static void RemoveNativeGrabFilter(PaperPageInstance page)
        {
            // Event removal operates on managed references and never enables a
            // collider, changes an interaction channel, or clears other filters.
            var pickup=page.Pickup;if(object.ReferenceEquals(pickup,null))return;
            var filter=(InteractionFilter)Delegate.CreateDelegate(typeof(InteractionFilter),page,originalGrabFilter);
            pickup.InteractionFilter-=filter;
        }
    }
}
