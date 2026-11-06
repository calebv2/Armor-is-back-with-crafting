using System;
using Alta.Impact;

namespace PlayerArmorMod
{
    internal static class ArmorProtectionPolicy
    {
        internal static bool IsProtectedImpact(ImpactType type)
        {return type==ImpactType.Pierce||type==ImpactType.Slash||type==ImpactType.Blunt||type==ImpactType.Explosive;}
        // These names are taken from the current native VR Player Character's
        // nine real detectors. No new colliders or grab targets are installed.
        internal static bool TryRegion(string name,out ArmorSlot slot)
        {
            slot=ArmorSlot.Head;
            switch(name)
            {
                case "Head":case "head_jnt":slot=ArmorSlot.Head;return true;
                case "Chest":case "chest_jnt":slot=ArmorSlot.Chest;return true;
                case "Cog":case "hipBase_jnt":slot=ArmorSlot.Hips;return true;
                case "shoulder_L_mov":case "shoulder_L_jnt":slot=ArmorSlot.LeftShoulder;return true;
                case "shoulder_R_mov":case "shoulder_R_jnt":slot=ArmorSlot.RightShoulder;return true;
                case "elbow_L_mov":case "wrist_L_mov":case "elbow_L_jnt":case "forearm_L_jnt":case "hand_L_jnt":slot=ArmorSlot.LeftForearm;return true;
                case "elbow_R_mov":case "wrist_R_mov":case "elbow_R_jnt":case "forearm_R_jnt":case "hand_R_jnt":slot=ArmorSlot.RightForearm;return true;
                default:return false;
            }
        }
    }
}
