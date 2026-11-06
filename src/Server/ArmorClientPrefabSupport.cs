using System;
using System.Collections.Generic;
using System.Reflection;
using Alta.Networking;
using SyncLib.Items;

namespace PlayerArmorMod
{
    internal static class ArmorClientPrefabSupport
    {
        const string LegacyPrefix = "[PLAYERARMOR24]";
        const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic;

        // SyncLib 1.1.1 already receives each connection's registered prefab
        // hashes before releasing custom entities to it. Read the report only.
        // Calling CanSpawn here would change SyncLib's pending spawn queue.
        static readonly Type Support = typeof(NetworkPrefabRegistry).Assembly.GetType("SyncLib.ClientPrefabSupport");
        static readonly FieldInfo States = Support?.GetField("states", Fields | BindingFlags.Static);
        static readonly MethodInfo TryGetState = States?.FieldType.GetMethod("TryGetValue");
        static readonly FieldInfo Hashes = Support?.GetNestedType("State", BindingFlags.NonPublic)?.GetField("Hashes", Fields | BindingFlags.Instance);

        internal static bool UsesLegacyWire(Connection connection, NetworkPrefab harness, NetworkPrefab lectern)
        {
            if (connection == null || harness == null || lectern == null || TryGetState == null || Hashes == null)
                return false;

            var arguments = new object[] { connection, null };
            if (!(TryGetState.Invoke(States.GetValue(null), arguments) is bool found) || !found || arguments[1] == null)
                return false;

            // 0.8.5 and 0.8.6 send the same hello. The 0.8.6 lectern is the
            // prefab that distinguishes these two compatible packet formats.
            var reported = Hashes.GetValue(arguments[1]) as HashSet<int>;
            return reported != null && reported.Contains((int)harness.Hash) && !reported.Contains((int)lectern.Hash);
        }

        internal static string ToLegacyWire(string wire)
        {
            return LegacyPrefix + wire.Substring(ArmorProtocol.Prefix.Length);
        }
    }
}
