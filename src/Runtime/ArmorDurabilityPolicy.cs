using System;

namespace PlayerArmorMod
{
    // Numeric policy shared by the native adapters and executable regressions.
    // Native DurabilityModule owns saved integrity and actual wear application.
    internal static class ArmorDurabilityPolicy
    {
        internal static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
        internal static float Condition(float value){return !Finite(value)?0f:Math.Max(0f,Math.Min(1f,value));}
        internal static float Wear(float condition,float maximum,float requested)
        {
            if(!Finite(maximum)||maximum<1||!Finite(requested)||requested<=0)return 0;
            return Math.Min(Condition(condition)*maximum,requested);
        }
        internal static float Reduction(float configured,float condition,bool durabilityEnabled)
        {
            if(!Finite(configured)||configured<=0||(durabilityEnabled&&Condition(condition)<=0))return 0;
            return Math.Min(.9f,configured);
        }
        internal static float Absorbed(float reducedDamage,float multiplier,float healthBefore,float healthAfter)
        {
            // Native player/PVP/invulnerability gates have already run before
            // HealthObject.ReceiveDamage. Do not wear armor on rejected/no-op hits.
            if(!Finite(reducedDamage)||reducedDamage<=0||!Finite(multiplier)||multiplier<=0||multiplier>=1
                ||!Finite(healthBefore)||!Finite(healthAfter)||healthAfter>=healthBefore)return 0;
            return Math.Max(0f,reducedDamage/multiplier-reducedDamage);
        }
    }
}
