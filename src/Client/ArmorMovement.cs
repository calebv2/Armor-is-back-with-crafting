using HarmonyLib;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static class ArmorMovement
    {
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            // CheckInput returns only controller-driven walking/running. Gravity,
            // climbing, room-scale head movement and moving platforms bypass it.
            harmony.Patch(AccessTools.Method(typeof(SmoothLocomotion),"CheckInput"),
                postfix:new HarmonyMethod(typeof(ArmorMovement),nameof(ScaleInput)));
        }
        static void ScaleInput(SmoothLocomotion __instance,ref Vector3 __result)
        {
            if(__instance==null||__instance.IsDebugMovement||__result.sqrMagnitude==0)return;
            __result*=ArmorClient.MovementMultiplier(__instance.Character);
        }
    }
}
