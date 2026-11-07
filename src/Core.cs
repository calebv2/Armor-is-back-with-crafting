using MelonLoader;
using UnityEngine;

namespace PlayerArmorMod
{
    public sealed class Core : MelonMod
    {
        internal static Core Instance;
        internal static bool IsServer { get { return Application.isBatchMode; } }
        internal ArmorPrefabRegistry Assets;
        ArmorServer server;
        ArmorClient client;
        public override void OnInitializeMelon()
        {
            Instance=this;
            if(IsServer)ArmorSettings.InitializeServer(MelonLoader.Utils.MelonEnvironment.UserDataDirectory);
            ArmorDurability.Install(HarmonyInstance);ArmorCraftRecord.Install(HarmonyInstance);ArmorCrafting.Install(HarmonyInstance);
            ArmorWeight.Install(HarmonyInstance);
            Assets=new ArmorPrefabRegistry(this);Assets.Subscribe();Assets.Preload();
            ArmorSurfaceGrip.Install(HarmonyInstance);
            if(IsServer)
            {
                MushroomChestLoot.Install(HarmonyInstance, Assets);
                WyrmArmorLoot.Install(HarmonyInstance, Assets);
                ArmorProtection.Install(HarmonyInstance);
                server=new ArmorServer(Assets);server.Install(HarmonyInstance);
            }
            else
            {
                client=new ArmorClient(Assets);client.Install(HarmonyInstance);
            }
            Log("Player Armor 0.9.8 initialized as "+(IsServer?"server":"client")+". Armor recipes use the dedicated Armor Workshop.");
        }
        public override void OnLateInitializeMelon(){if(Assets!=null)Assets.Preload();}
        public override void OnUpdate()
        {
            ArmorCrafting.Update();
            if(server!=null){ArmorSettings.Poll(Time.unscaledTime);server.Update();}
            if(client!=null)client.Update();
        }
        public override void OnLateUpdate()
        {
            if(client!=null)client.LateUpdate();
        }
        public override void OnDeinitializeMelon()
        {
            if(IsServer)
            {
                MushroomChestLoot.Dispose();
                WyrmArmorLoot.Dispose();
                if(server!=null)server.Dispose();ArmorProtection.Dispose();
            }
            else
            {
                if(client!=null)client.Dispose();ArmorStereo.Dispose();
            }
            ArmorCrafting.Dispose();ArmorCraftRecord.Dispose();ArmorDurability.Dispose();Instance=null;
            ArmorSurfaceGrip.Dispose();
        }
        internal static void Log(string value){if(Instance!=null)Instance.LoggerInstance.Msg(value);}
        internal static void Error(string value){if(Instance!=null)Instance.LoggerInstance.Error(value);}
    }
}
