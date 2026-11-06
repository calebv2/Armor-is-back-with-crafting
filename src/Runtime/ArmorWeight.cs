using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static class ArmorWeight
    {
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(Pickup), nameof(Pickup.PhysicalWeight)),
                postfix: new HarmonyMethod(typeof(ArmorWeight), nameof(UseArmorWeight)));
#if SERVER
            harmony.Patch(AccessTools.Method(typeof(PlayerWeight), "GetInteractablesWeight"),
                postfix: new HarmonyMethod(typeof(ArmorWeight), nameof(IncludeWornWeight)));
#endif
        }

        static void UseArmorWeight(Pickup __instance, ref float __result)
        {
            var armor = __instance.GetComponent<ArmorPickup>();
            if (armor != null && armor.Weight > 0) __result = armor.Weight;
        }

        internal static void Refresh(ArmorPickup armor, float multiplier)
        {
            if (armor == null) return;
            float weight = ArmorWeightPolicy.EffectiveWeight(ArmorWeightPolicy.BaseWeight(armor.Style, (ArmorSlot)armor.Slot), multiplier);
            if (armor.Weight == weight) return;
            armor.Weight = weight;
            var body = armor.GetComponent<Rigidbody>();
            if (body != null) body.mass = weight;
            var pickup = armor.GetComponent<Pickup>();
            if (pickup == null) return;
            pickup.OnWeightChanged();
#if SERVER
            // Saved profiles may load before Pickup has initialized its native dock state.
            if (armor.Entity != null && armor.Entity.IsInitialized)
            {
                var dock = pickup.Dock;
                var harness = dock == null ? null : dock.GetComponentInParent<ArmorHarness>();
                if (harness != null) RefreshWearer(harness.Owner);
            }
#endif
        }

#if SERVER
        static readonly MethodInfo requestWeightUpdate = AccessTools.Method(typeof(PlayerWeight), "OnWeightChanged");

        static void IncludeWornWeight(PlayerWeight __instance, PlayerController ___player, ref float __result)
        {
            __result += ArmorServer.WornWeight(___player, __instance);
        }

        internal static void RefreshWearer(PlayerController player)
        {
            if (player == null || player.Entity == null || !player.Entity.IsInitialized || player.Entity.IsBeingDestroyed) return;
            var stat = player.GetComponent<PlayerWeight>();
            if (stat != null) requestWeightUpdate.Invoke(stat, null);
        }
#endif
    }
}
