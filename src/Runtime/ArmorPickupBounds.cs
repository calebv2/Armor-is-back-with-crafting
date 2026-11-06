using System;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static class ArmorPickupBounds
    {
        // Individual loose armor pieces are smaller than one metre. Leave room
        // for legitimate art while rejecting displaced source-pose geometry.
        const float MaximumDimension=1.5f;
        const float MaximumCenterOffset=1.5f;

        internal static Bounds Validate(Bounds bounds,string prefabName)
        {
            var center=bounds.center;var size=bounds.size;
            if(!Finite(center.x)||!Finite(center.y)||!Finite(center.z)
                ||!Finite(size.x)||!Finite(size.y)||!Finite(size.z)
                ||size.x<=0||size.y<=0||size.z<=0
                ||size.x>MaximumDimension||size.y>MaximumDimension||size.z>MaximumDimension
                ||Math.Abs(center.x)>MaximumCenterOffset||Math.Abs(center.y)>MaximumCenterOffset||Math.Abs(center.z)>MaximumCenterOffset)
                throw new InvalidOperationException("Unsafe loose armor bounds for "+(prefabName??"<unknown>")+": center="+center+", size="+size
                    +". Expected finite positive dimensions at most 1.5m and center coordinates within +/-1.5m.");
            return bounds;
        }

        static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
    }
}
