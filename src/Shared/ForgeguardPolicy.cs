using System;

namespace PlayerArmorMod
{
    internal static class ForgeguardPolicy
    {
        internal static float Penalty(ArmorSlot slot)
        {
            switch(slot)
            {
                case ArmorSlot.Head:return .03f;
                case ArmorSlot.Chest:return .09f;
                case ArmorSlot.LeftShoulder:case ArmorSlot.RightShoulder:return .045f;
                case ArmorSlot.LeftForearm:case ArmorSlot.RightForearm:return .02f;
                case ArmorSlot.Hips:return .05f;
                default:throw new ArgumentOutOfRangeException("slot");
            }
        }
        internal static float MovementMultiplier(byte occupied,uint styles)
        {
            float penalty=0;
            for(int i=0;i<ArmorCatalog.SlotCount;i++)
                if((occupied&(1<<i))!=0)
                    penalty+=ArmorSettings.GetPiece(ArmorCatalog.StyleAt(styles,(ArmorSlot)i),(ArmorSlot)i).movementPenalty;
            return Math.Max(.1f,1f-penalty);
        }
    }
}
