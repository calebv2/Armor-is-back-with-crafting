using System;
using System.Reflection;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using ATT.Character.QuickAccessMenu;

namespace PlayerArmorMod
{
    // Player Armor's small protocol uses the native authenticated message
    // channel. TavernLib intentionally cancels the public console RouteCommand
    // entry point; no global console/security patch is changed here.
    internal static class ArmorClientCommandBridge
    {
        static readonly FieldInfo commandField=typeof(CommandSync).GetField("commandToSync",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly FieldInfo syncField=typeof(CommandSync).GetField("syncCommand",BindingFlags.Instance|BindingFlags.NonPublic);
        static bool reportedFailure;

        internal static bool Send(string message)
        {
            // This bridge is never an arbitrary console-command sender. The
            // server independently parses the same protocol and derives the
            // owner from its authenticated IPlayer argument, not this text.
            ArmorCommand parsed;
            if(!ArmorProtocol.TryParseCommand(message,out parsed)||NetworkSceneManager.IsServer)return false;
            var sync=CommandSync.Instance;
            if(sync==null||Player.Current==null)return false;
            if(commandField==null||commandField.FieldType!=typeof(string)||syncField==null||syncField.FieldType!=typeof(MethodSync))
            {Report("Native CommandSync fields are incompatible.");return false;}
            try
            {
                var native=syncField.GetValue(sync) as MethodSync;
                if(native==null)return false; // Its native Initialize may still be pending.
                commandField.SetValue(sync,message);
                native.SendToChunks(new IPlayer[0]);
                return true;
            }
            catch(Exception error){Report(error.GetBaseException().Message);return false;}
        }
        static void Report(string message)
        {
            if(reportedFailure)return;reportedFailure=true;
            Core.Error("PLAYER_ARMOR_TRANSPORT_FAILED "+message);
        }
    }
}
