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

        internal static bool TryReadCapabilities(Connection connection, NetworkPrefab harness, NetworkPrefab lectern,
            NetworkPrefab crystalHelmet, NetworkPrefab[] standaloneHelmets, NetworkPrefab forgeguardHelmet,
            out bool legacyWire, out ArmorStyle latestStyle)
        {
            var reported = ReportedHashes(connection);
            // 0.8.5 and 0.8.6 send the same hello. The 0.8.6 lectern is the
            // prefab that distinguishes these two compatible packet formats.
            legacyWire = reported != null && harness != null && lectern != null
                && reported.Contains((int)harness.Hash) && !reported.Contains((int)lectern.Hash);
            latestStyle = ArmorStyle.Wyrm;
            if (reported != null)
            {
                if (crystalHelmet != null && reported.Contains((int)crystalHelmet.Hash))
                    latestStyle = ArmorStyle.CrystalWyrm;
                // Each release appends helmets. A client without the latest
                // one can still receive the standalone helmets it registered.
                for (int i = standaloneHelmets.Length - 1; i >= 0; i--)
                {
                    var helmet = standaloneHelmets[i];
                    if (helmet == null || !reported.Contains((int)helmet.Hash)) continue;
                    latestStyle = ArmorCatalog.StandaloneHelmets[i].Style;
                    break;
                }
                if (forgeguardHelmet != null && reported.Contains((int)forgeguardHelmet.Hash))
                    latestStyle = ArmorStyle.Forgeguard;
            }
            // A report can span several packets. Let the client's normal hello
            // retry wait for it before committing this session's wire format.
            return reported != null || connection == null || TryGetState == null || Hashes == null;
        }

        static HashSet<int> ReportedHashes(Connection connection)
        {
            if (connection == null || TryGetState == null || Hashes == null)
                return null;

            var arguments = new object[] { connection, null };
            if (!(TryGetState.Invoke(States.GetValue(null), arguments) is bool found) || !found || arguments[1] == null)
                return null;

            return Hashes.GetValue(arguments[1]) as HashSet<int>;
        }

        internal static string ToLegacyWire(string wire)
        {
            return LegacyPrefix + wire.Substring(ArmorProtocol.Prefix.Length);
        }
    }
}
