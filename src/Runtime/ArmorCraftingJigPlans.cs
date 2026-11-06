using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PlayerArmorMod
{
    [Serializable]
    public sealed class ArmorJigArchive
    {
        public int schemaVersion;
        public string bundleSha256;
        public ArmorJigArchiveEntry[] entries;
    }

    [Serializable]
    public sealed class ArmorJigArchiveEntry
    {
        public string prefabName;
        public uint[] sources;
        public int[] levels,parents;
        public bool[] requiresNails;
        public Vector3[] nailPositions,jointPoints,supportPoints;
        public Quaternion[] nailRotations;
        public float[] jointGaps;
        public int[] clearanceDegrees;
    }

    // The verified authoring result is shared byte-for-byte by the client and
    // server. Production never repeats the floating-point seam/ordering search.
    internal static class ArmorCraftingJigPlans
    {
        static int EntryCount{get{return ArmorCatalog.RecipePagePieces.Count;}}
        const int MaximumJsonBytes=1024*1024;
        const string ArchiveResource="PlayerArmor.JigPlans",ModelResource="PlayerArmor.Model";
        static readonly object gate=new object();
        static Dictionary<string,ArmorJigArchiveEntry> loaded;
        static string modelSha256;

        static InvalidDataException Invalid(string message){return new InvalidDataException("Armor jig archive: "+message);}
        static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
        static bool Finite(Vector3 value){return Finite(value.x)&&Finite(value.y)&&Finite(value.z);}
        static bool Unit(Quaternion value)
        {
            if(!Finite(value.x)||!Finite(value.y)||!Finite(value.z)||!Finite(value.w))return false;
            double length=(double)value.x*value.x+(double)value.y*value.y+(double)value.z*value.z+(double)value.w*value.w;
            return Math.Abs(length-1)<=.0001;
        }
        static bool Digest(string value)
        {
            if(value==null||value.Length!=64)return false;
            foreach(char c in value)if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')||(c>='A'&&c<='F')))return false;return true;
        }
        static ArmorPieceDefinition Piece(string name)
        {
            if(string.IsNullOrEmpty(name))throw Invalid("entry has no prefab name");
            foreach(var piece in ArmorCatalog.RecipePagePieces)if(string.Equals(name,piece.PrefabName,StringComparison.Ordinal))return piece;
            throw Invalid("unknown armor prefab "+name);
        }
        static T[] Copy<T>(T[] source,string field)
        {if(source==null)throw Invalid("missing "+field);return(T[])source.Clone();}
        static void Count<T>(T[] values,int expected,string field,string name)
        {if(values==null||values.Length!=expected)throw Invalid(name+" has invalid "+field+" count");}
        static uint[] ExpectedSources(ArmorPieceDefinition piece)
        {
            var result=new List<uint>();uint primary=ArmorConstructionPlan.PrimarySource(piece.Style),closure=ArmorConstructionPlan.FastenerSource(piece.Style);
            for(int i=0;i<ArmorConstructionPlan.PrimaryCount(piece.Style,piece.Slot);i++)result.Add(primary);
            for(int i=0;i<ArmorConstructionPlan.FastenerCount(piece.Style,piece.Slot);i++)result.Add(closure);
            result.Sort((a,b)=>string.CompareOrdinal(a.ToString(CultureInfo.InvariantCulture),b.ToString(CultureInfo.InvariantCulture)));return result.ToArray();
        }
        static bool Equal(uint[] a,uint[] b)
        {if(a==null||b==null||a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
        static double Separation(Vector3 a,Vector3 b)
        {double x=(double)a.x-b.x,y=(double)a.y-b.y,z=(double)a.z-b.z;return Math.Sqrt(x*x+y*y+z*z);}
        static void Validate(ArmorJigArchiveEntry entry)
        {
            if(entry==null)throw Invalid("null entry");var piece=Piece(entry.prefabName);var expected=ExpectedSources(piece);int count=expected.Length;
            if(!Equal(entry.sources,expected))throw Invalid(entry.prefabName+" sources are not the complete canonical native ingredient order");
            Count(entry.levels,count,"levels",entry.prefabName);Count(entry.parents,count,"parents",entry.prefabName);Count(entry.requiresNails,count,"requiresNails",entry.prefabName);
            Count(entry.nailPositions,count,"nailPositions",entry.prefabName);Count(entry.nailRotations,count,"nailRotations",entry.prefabName);
            Count(entry.jointPoints,count,"jointPoints",entry.prefabName);Count(entry.supportPoints,count,"supportPoints",entry.prefabName);Count(entry.jointGaps,count,"jointGaps",entry.prefabName);Count(entry.clearanceDegrees,count,"clearanceDegrees",entry.prefabName);
            bool twoWings=piece.Style==ArmorStyle.Metal&&piece.Slot==ArmorSlot.Hips;int roots=twoWings?2:1,maxLevel=count-roots;
            var stageCounts=new int[maxLevel+1];var primaryIndices=new List<int>();int rootCount=0;
            uint primary=ArmorConstructionPlan.PrimarySource(piece.Style);
            for(int i=0;i<count;i++)if(entry.sources[i]==primary)primaryIndices.Add(i);
            for(int i=0;i<count;i++)
            {
                string at=entry.prefabName+" part "+i;int level=entry.levels[i],parent=entry.parents[i];bool root=parent==-1;
                if(level<0||level>maxLevel)throw Invalid(at+" has an invalid stage");stageCounts[level]++;
                if(parent < -1||parent>=count||parent==i)throw Invalid(at+" has an invalid parent");
                if(!Finite(entry.nailPositions[i])||!Finite(entry.jointPoints[i])||!Finite(entry.supportPoints[i])||!Unit(entry.nailRotations[i]))throw Invalid(at+" has nonfinite geometry or a nonunit nail rotation");
                float gap=entry.jointGaps[i];if(!Finite(gap)||gap<0||gap>ArmorCraftingMesh.JigMaximumJointGap)throw Invalid(at+" exceeds the real 15mm joint-gap limit");
                bool isPrimary=entry.sources[i]==primary,ordinaryNail=entry.sources[i]!=43836u;
                if(root)
                {
                    rootCount++;if(!isPrimary||level!=0||entry.requiresNails[i]||gap!=0)throw Invalid(at+" is not a zero-nail primary seat at level0");
                }
                else
                {
                    if(level==0||entry.levels[parent]>=level)throw Invalid(at+" must join an earlier completed stage");
                    if(entry.requiresNails[i]!=ordinaryNail)throw Invalid(at+" has an invalid native nail mask");
                    double distance=Separation(entry.jointPoints[i],entry.supportPoints[i]);
                    if(distance>ArmorCraftingMesh.JigMaximumJointGap+.000001||Math.Abs(distance-gap)>.00001)throw Invalid(at+" has inconsistent or unsupported seam witnesses");
                }
                int clearance=entry.clearanceDegrees[i];
                if(entry.requiresNails[i]){if(clearance!=0&&clearance!=20&&clearance!=45)throw Invalid(at+" has no verified native hammer clearance");}
                else if(clearance!=-2)throw Invalid(at+" assigns hammer clearance to a piece without nails");
            }
            if(rootCount!=roots||stageCounts[0]!=roots)throw Invalid(entry.prefabName+" has the wrong independent seat count");
            for(int level=1;level<stageCounts.Length;level++)if(stageCounts[level]!=1)throw Invalid(entry.prefabName+" joining stages must be contiguous and unique");
            if(twoWings)
            {
                if(primaryIndices.Count!=4||count!=5)throw Invalid("Metal hip jig must contain exactly two native two-panel wings and one closure");
                for(int wing=0;wing<2;wing++)
                {
                    int a=primaryIndices[wing*2],b=primaryIndices[wing*2+1];bool firstRoot=entry.parents[a]==-1,secondRoot=entry.parents[b]==-1;
                    if(firstRoot==secondRoot)throw Invalid("Each metal hip wing must have exactly one independent level0 seat");
                    int anchor=firstRoot?a:b,joined=firstRoot?b:a;
                    if(entry.parents[joined]!=anchor||!entry.requiresNails[joined])throw Invalid("Metal hip panels must rivet directly to their own seated wing");
                }
                // A closure may be fastened before its covering panel while
                // that future panel has no active native workpiece collider.
                // The two actual wing hinges must still join their own roots.
            }
        }

        internal static ArmorJigArchiveEntry FromPlan(string name,uint[] sources,ArmorJigPlan plan,int[] degrees)
        {
            if(plan==null)throw new ArgumentNullException("plan");
            var entry=new ArmorJigArchiveEntry{prefabName=name,sources=Copy(sources,"sources"),levels=Copy(plan.Levels,"levels"),parents=Copy(plan.Parents,"parents"),requiresNails=Copy(plan.RequiresNails,"requiresNails"),nailPositions=Copy(plan.NailPositions,"nailPositions"),nailRotations=Copy(plan.NailRotations,"nailRotations"),jointPoints=Copy(plan.JointPoints,"jointPoints"),supportPoints=Copy(plan.SupportPoints,"supportPoints"),jointGaps=Copy(plan.JointGaps,"jointGaps"),clearanceDegrees=Copy(degrees,"clearanceDegrees")};
            Validate(entry);return entry;
        }

        internal static Dictionary<string,ArmorJigArchiveEntry> Parse(string json,string expectedBundleSha)
        {
            if(string.IsNullOrEmpty(json)||Encoding.UTF8.GetByteCount(json)>MaximumJsonBytes)throw Invalid("JSON is empty or exceeds one MiB");
            if(!Digest(expectedBundleSha))throw Invalid("expected model SHA256 is invalid");
            ArmorJigArchive archive;
            // ATT's runtime may give Unity structs a string-only JSON contract.
            // Read this numeric archive without asking Json.NET to reflect or
            // convert Vector3/Quaternion, and without global DefaultSettings.
            try{archive=ReadArchive(json);}
            catch(Exception error){throw new InvalidDataException("Armor jig archive JSON could not be read",error);}
            if(archive==null||archive.schemaVersion!=1||archive.entries==null||archive.entries.Length!=EntryCount)throw Invalid("schema1 requires exactly "+EntryCount+" armor entries");
            if(!Digest(archive.bundleSha256)||!string.Equals(archive.bundleSha256,expectedBundleSha,StringComparison.OrdinalIgnoreCase))throw Invalid("verified jig data does not match the embedded armor model SHA256");
            var result=new Dictionary<string,ArmorJigArchiveEntry>(StringComparer.Ordinal);
            foreach(var entry in archive.entries)
            {Validate(entry);if(result.ContainsKey(entry.prefabName))throw Invalid("duplicate armor prefab "+entry.prefabName);result.Add(entry.prefabName,entry);}
            foreach(var piece in ArmorCatalog.RecipePagePieces)if(!result.ContainsKey(piece.PrefabName))throw Invalid("missing armor prefab "+piece.PrefabName);return result;
        }

        static JObject Object(JToken token,string name,params string[] fields)
        {
            var value=token as JObject;if(value==null)throw Invalid(name+" must be an object");
            var allowed=new HashSet<string>(fields,StringComparer.Ordinal);
            foreach(var property in value.Properties())if(!allowed.Contains(property.Name))throw Invalid(name+" contains unknown field "+property.Name);
            foreach(string field in fields)if(value.Property(field)==null)throw Invalid(name+" is missing "+field);
            return value;
        }
        static JArray Array(JToken token,string name)
        {var value=token as JArray;if(value==null)throw Invalid(name+" must be an array");return value;}
        static string Text(JToken token,string name)
        {if(token==null||token.Type!=JTokenType.String)throw Invalid(name+" must be a string");return(string)token;}
        static int Integer(JToken token,string name)
        {
            int result;if(token==null||token.Type!=JTokenType.Integer||!int.TryParse(token.ToString(Formatting.None),NumberStyles.Integer,CultureInfo.InvariantCulture,out result))throw Invalid(name+" must be an Int32");return result;
        }
        static uint Unsigned(JToken token,string name)
        {
            uint result;if(token==null||token.Type!=JTokenType.Integer||!uint.TryParse(token.ToString(Formatting.None),NumberStyles.None,CultureInfo.InvariantCulture,out result))throw Invalid(name+" must be a UInt32");return result;
        }
        static float Number(JToken token,string name)
        {
            if(token==null||(token.Type!=JTokenType.Integer&&token.Type!=JTokenType.Float))throw Invalid(name+" must be a number");
            double value=(double)token;if(double.IsNaN(value)||double.IsInfinity(value)||value>float.MaxValue||value < -float.MaxValue)throw Invalid(name+" must be finite");
            float result=(float)value;if(!Finite(result))throw Invalid(name+" must be finite");return result;
        }
        static T[] Values<T>(JToken token,string name,Func<JToken,string,T> read)
        {
            var array=Array(token,name);var result=new T[array.Count];for(int i=0;i<result.Length;i++)result[i]=read(array[i],name+"["+i+"]");return result;
        }
        static bool Boolean(JToken token,string name)
        {if(token==null||token.Type!=JTokenType.Boolean)throw Invalid(name+" must be boolean");return(bool)token;}
        static Vector3 Vector(JToken token,string name)
        {
            var value=Object(token,name,"x","y","z");return new Vector3(Number(value["x"],name+".x"),Number(value["y"],name+".y"),Number(value["z"],name+".z"));
        }
        static Quaternion Rotation(JToken token,string name)
        {
            var value=Object(token,name,"x","y","z","w");return new Quaternion(Number(value["x"],name+".x"),Number(value["y"],name+".y"),Number(value["z"],name+".z"),Number(value["w"],name+".w"));
        }
        static ArmorJigArchive ReadArchive(string json)
        {
            JObject root;
            using(var text=new StringReader(json))using(var reader=new JsonTextReader(text){MaxDepth=8,DateParseHandling=DateParseHandling.None,FloatParseHandling=FloatParseHandling.Double})
            {
                root=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error,CommentHandling=CommentHandling.Ignore});
                while(reader.Read())if(reader.TokenType!=JsonToken.Comment)throw Invalid("JSON contains trailing content");
            }
            root=Object(root,"archive","schemaVersion","bundleSha256","entries");
            var source=Array(root["entries"],"entries");var entries=new ArmorJigArchiveEntry[source.Count];
            for(int i=0;i<entries.Length;i++)
            {
                string at="entries["+i+"]";var entry=Object(source[i],at,"prefabName","sources","levels","parents","requiresNails","nailPositions","nailRotations","jointPoints","supportPoints","jointGaps","clearanceDegrees");
                entries[i]=new ArmorJigArchiveEntry{prefabName=Text(entry["prefabName"],at+".prefabName"),sources=Values(entry["sources"],at+".sources",Unsigned),levels=Values(entry["levels"],at+".levels",Integer),parents=Values(entry["parents"],at+".parents",Integer),requiresNails=Values(entry["requiresNails"],at+".requiresNails",Boolean),nailPositions=Values(entry["nailPositions"],at+".nailPositions",Vector),nailRotations=Values(entry["nailRotations"],at+".nailRotations",Rotation),jointPoints=Values(entry["jointPoints"],at+".jointPoints",Vector),supportPoints=Values(entry["supportPoints"],at+".supportPoints",Vector),jointGaps=Values(entry["jointGaps"],at+".jointGaps",Number),clearanceDegrees=Values(entry["clearanceDegrees"],at+".clearanceDegrees",Integer)};
            }
            return new ArmorJigArchive{schemaVersion=Integer(root["schemaVersion"],"schemaVersion"),bundleSha256=Text(root["bundleSha256"],"bundleSha256"),entries=entries};
        }

        static void Load()
        {
            if(loaded!=null)return;var assembly=typeof(ArmorCraftingJigPlans).Assembly;
            if(modelSha256==null)using(var model=assembly.GetManifestResourceStream(ModelResource))
            {
                if(model==null)throw Invalid("embedded armor model is missing");
                using(var sha=SHA256.Create()){var bytes=sha.ComputeHash(model);var text=new StringBuilder(64);foreach(byte b in bytes)text.Append(b.ToString("x2",CultureInfo.InvariantCulture));modelSha256=text.ToString();}
            }
            using(var resource=assembly.GetManifestResourceStream(ArchiveResource))
            {
                if(resource==null)throw Invalid("embedded verified plans are missing");
                if(resource.CanSeek&&resource.Length>MaximumJsonBytes)throw Invalid("embedded plans exceed one MiB");
                using(var reader=new StreamReader(resource,new UTF8Encoding(false,true)))loaded=Parse(reader.ReadToEnd(),modelSha256);
            }
        }
        internal static void VerifyEmbedded(){lock(gate)Load();}
        internal static ArmorJigPlan Read(ArmorPieceDefinition piece,uint[] sourceHashes)
        {
            if(piece==null)throw new ArgumentNullException("piece");var canonical=Piece(piece.PrefabName);
            if(canonical.Style!=piece.Style||canonical.Slot!=piece.Slot)throw Invalid("armor style/slot differs from its canonical prefab");
            lock(gate)
            {
                Load();ArmorJigArchiveEntry entry;if(!loaded.TryGetValue(piece.PrefabName,out entry))throw Invalid("verified prefab plan is unavailable");
                if(!Equal(entry.sources,sourceHashes))throw Invalid(piece.PrefabName+" native source order differs from its verified jig plan");
                var plan=new ArmorJigPlan{Levels=Copy(entry.levels,"levels"),Parents=Copy(entry.parents,"parents"),RequiresNails=Copy(entry.requiresNails,"requiresNails"),NailPositions=Copy(entry.nailPositions,"nailPositions"),NailRotations=Copy(entry.nailRotations,"nailRotations"),JointPoints=Copy(entry.jointPoints,"jointPoints"),SupportPoints=Copy(entry.supportPoints,"supportPoints"),JointGaps=Copy(entry.jointGaps,"jointGaps")};
                // Match the synchronous authoring planner contract without
                // exposing any cached archive array to callers.
                ArmorCraftingMesh.LastNailClearanceDegrees=Copy(entry.clearanceDegrees,"clearanceDegrees");return plan;
            }
        }
        internal static void Dispose(){lock(gate)loaded=null;}
    }
}
