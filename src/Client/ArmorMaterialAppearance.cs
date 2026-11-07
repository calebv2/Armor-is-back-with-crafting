using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace PlayerArmorMod
{
    // Own the per-instance renderer bindings and cache their generated meshes and materials.
    internal sealed class ArmorMaterialAppearance:IDisposable
    {
        sealed class CachedMesh {internal Mesh Mesh;internal Material[] Materials,OwnedMaterials;internal Action<Material> ReleaseMaterial;internal bool VertexColors;internal int References;}
        sealed class Binding
        {
            internal Renderer Renderer;internal MeshFilter Filter;internal SkinnedMeshRenderer Skin;
            internal Mesh Source;internal Material[] OriginalMaterials;internal MaterialPropertyBlock Original;internal string Key;
        }
        static readonly Dictionary<string,CachedMesh> meshes=new Dictionary<string,CachedMesh>();
        readonly List<Binding> bindings=new List<Binding>();
        readonly Func<uint,ArmorStyle,Color?> resolve;
        readonly ArmorStyle style;
        uint revision=uint.MaxValue;string applied;float retry;
        uint requestedLayout=1;
        int requestedPrimaryCount;
        bool requestedTintFasteners=true;
        uint requestedRevision=uint.MaxValue;uint[] requestedHashes=new uint[0];
        bool disposed;
        internal ArmorMaterialAppearance(Renderer[] renderers,Func<uint,Color?> resolver)
            :this(renderers,ArmorStyle.Metal,(hash,_)=>resolver(hash)){}
        internal ArmorMaterialAppearance(Renderer[] renderers,ArmorStyle style,Func<uint,ArmorStyle,Color?> resolver)
        {
            this.style=style;
            resolve=resolver??throw new ArgumentNullException("resolver");
            foreach(var renderer in renderers)
            {
                if(renderer==null)continue;var skin=renderer as SkinnedMeshRenderer;var filter=skin==null?renderer.GetComponent<MeshFilter>():null;
                var source=skin!=null?skin.sharedMesh:filter==null?null:filter.sharedMesh;
                if(!ArmorSemanticLayout.HasMetadata(source)&&!ArmorMaterialMesh.HasCoordinates(source))continue;
                var original=new MaterialPropertyBlock();renderer.GetPropertyBlock(original);
                bindings.Add(new Binding{Renderer=renderer,Filter=filter,Skin=skin,Source=source,OriginalMaterials=renderer.sharedMaterials,Original=original});
            }
        }
        internal void Update(IList<uint> hashes,uint profileRevision,uint layoutVersion=1,int primaryCount=0,bool tintFasteners=true)
        {
            if(disposed)return;
            if(hashes!=null&&hashes.Count>32){Restore();return;}
            bool changed=tintFasteners!=requestedTintFasteners||primaryCount!=requestedPrimaryCount||layoutVersion!=requestedLayout||profileRevision!=requestedRevision||!SameHashes(hashes,requestedHashes);
            if(changed)
            {
                requestedTintFasteners=tintFasteners;requestedPrimaryCount=primaryCount;requestedLayout=layoutVersion;requestedRevision=profileRevision;requestedHashes=new uint[hashes==null?0:hashes.Count];
                for(int i=0;i<requestedHashes.Length;i++)requestedHashes[i]=hashes[i];Restore();retry=0;revision=uint.MaxValue;
            }
            if(hashes==null||hashes.Count==0||layoutVersion!=2&&!ArmorCatalog.UsesMetalMaterials(style))
            {Restore();revision=profileRevision;return;}
            if(profileRevision==revision&&applied!=null)return;
            if(profileRevision!=revision&&applied!=null)Restore();
            if(Time.unscaledTime<retry)return;retry=Time.unscaledTime+1f;
#if !UNITY_EDITOR
            if(style==ArmorStyle.Woodland)
            {
                if(hashes.Count!=primaryCount)return;
                var woods=new Material[hashes.Count];
                for(int i=0;i<woods.Length;i++)
                {
                    try{woods[i]=ArmorMaterialPalette.ResolveWood(hashes[i]);}catch{return;}
                    if(woods[i]==null)return;
                }
                ApplyWood(woods);revision=profileRevision;return;
            }
            if(style==ArmorStyle.Leather||style==ArmorStyle.Hebios&&layoutVersion==ArmorSemanticLayout.Version)
            {
                if(primaryCount<1||hashes.Count<primaryCount)return;
                int metalCount=style==ArmorStyle.Hebios?primaryCount:0;
                var leathers=new Material[hashes.Count];
                for(int i=metalCount;i<leathers.Length;i++)
                {
                    try{leathers[i]=ArmorMaterialPalette.ResolveLeather(hashes[i]);}catch{return;}
                    if(leathers[i]==null)return;
                }
                var metals=ReadColors(hashes,metalCount);
                if(metals==null)return;
                ApplyLeather(leathers,metals,primaryCount);revision=profileRevision;return;
            }
#endif
            var palette=ReadColors(hashes,hashes.Count);
            if(palette==null)return;
            Apply(palette,layoutVersion,primaryCount,tintFasteners);revision=profileRevision;
        }
        Color[] ReadColors(IList<uint> hashes,int count)
        {
            var palette=new Color[count];
            for(int i=0;i<palette.Length;i++)
            {
                Color? color;try{color=resolve(hashes[i],style);}catch{return null;}
                if(!color.HasValue)return null;palette[i]=color.Value;
            }
            return palette;
        }
        static bool SameHashes(IList<uint> source,uint[] target)
        {if(source==null)return target.Length==0;if(source.Count!=target.Length)return false;for(int i=0;i<target.Length;i++)if(source[i]!=target[i])return false;return true;}
        internal void Apply(Color[] palette,uint layoutVersion=1,int primaryCount=0,bool tintFasteners=true)
        {
            if(disposed)return;
            if(palette==null||palette.Length<1||palette.Length>32)throw new ArgumentException("Armor color palette must contain one to thirty-two colors","palette");
            var signature=new StringBuilder().Append(style).Append(':').Append(layoutVersion).Append(':').Append(primaryCount).Append(':').Append(tintFasteners).Append(':');
            AppendColors(signature,palette);
            ApplyBindings(signature.ToString(),layoutVersion,false,binding=>new CachedMesh{
                Mesh=ArmorMaterialMesh.Build(binding.Source,palette,layoutVersion,primaryCount,tintFasteners,style),VertexColors=true});
        }
        static void AppendColors(StringBuilder signature,Color[] palette)
        {
            foreach(var color in palette)signature.Append(color.r.ToString("R",CultureInfo.InvariantCulture)).Append(',').Append(color.g.ToString("R",CultureInfo.InvariantCulture)).Append(',').Append(color.b.ToString("R",CultureInfo.InvariantCulture)).Append(',').Append(color.a.ToString("R",CultureInfo.InvariantCulture)).Append(';');
        }
#if !UNITY_EDITOR
        void ApplyWood(Material[] woods)
        {
            var signature=new StringBuilder("Woodland:");
            foreach(var wood in woods)signature.Append(wood.GetInstanceID()).Append(':');
            ApplyBindings(signature.ToString(),ArmorSemanticLayout.Version,true,binding=>
            {
                var entry=new CachedMesh{ReleaseMaterial=ArmorWoodSurface.Release};
                entry.Mesh=ArmorWoodSurface.Build(binding.Source,binding.OriginalMaterials,woods,out entry.Materials,out entry.OwnedMaterials);
                return entry;
            });
        }
        void ApplyLeather(Material[] leathers,Color[] metals,int primaryCount)
        {
            var signature=new StringBuilder().Append(style).Append(":leather:").Append(primaryCount).Append(':');
            foreach(var leather in leathers)signature.Append(leather==null?0:leather.GetInstanceID()).Append(':');
            AppendColors(signature,metals);
            ApplyBindings(signature.ToString(),ArmorSemanticLayout.Version,true,binding=>
            {
                var entry=new CachedMesh{ReleaseMaterial=ArmorLeatherSurface.Release};
                Mesh tinted=null;
                try
                {
                    if(metals.Length>0)
                    {
                        tinted=ArmorMaterialMesh.Build(binding.Source,metals,ArmorSemanticLayout.Version,primaryCount,false,style);
                        entry.VertexColors=true;
                    }
                    entry.Mesh=ArmorLeatherSurface.Build(tinted??binding.Source,binding.OriginalMaterials,leathers,primaryCount,style,out entry.Materials,out entry.OwnedMaterials);
                    if(entry.Mesh==null){entry.Mesh=tinted;tinted=null;}
                    return entry;
                }
                finally{Destroy(tinted);}
            });
        }
#endif
        void ApplyBindings(string next,uint layoutVersion,bool keyMaterials,Func<Binding,CachedMesh> build)
        {
            try
            {
                if(applied==next)return;
                Restore();
                foreach(var binding in bindings)
                {
                    if(binding.Renderer==null||binding.Source==null)continue;
                    if(layoutVersion==2?!ArmorSemanticLayout.HasMetadata(binding.Source):!ArmorMaterialMesh.HasCoordinates(binding.Source))continue;
                    string key=binding.Source.GetInstanceID().ToString(CultureInfo.InvariantCulture)+":"+next;
                    if(keyMaterials)foreach(var material in binding.OriginalMaterials)key+=":"+(material==null?0:material.GetInstanceID()).ToString(CultureInfo.InvariantCulture);
                    CachedMesh entry;
                    if(!meshes.TryGetValue(key,out entry))
                    {
                        entry=build(binding);
                        if(entry.Mesh==null)continue;
                        try{meshes.Add(key,entry);}catch{Destroy(entry);throw;}
                    }
                    entry.References++;binding.Key=key;
                    if(binding.Skin!=null)binding.Skin.sharedMesh=entry.Mesh;else binding.Filter.sharedMesh=entry.Mesh;
                    if(entry.Materials!=null)binding.Renderer.sharedMaterials=entry.Materials;
                    if(entry.VertexColors)
                    {
                        var block=new MaterialPropertyBlock();binding.Renderer.GetPropertyBlock(block);block.SetFloat("_UseVertexColor",1);block.SetFloat("_Glow",0);binding.Renderer.SetPropertyBlock(block);
                    }
                }
                applied=next;
            }
            catch(Exception error)
            {
                // A failure on a later renderer must not leave earlier ones
                // using a partial palette, an owned mesh or a property block.
                Exception rollbackError=null;
                try{Restore();}catch(Exception failure){rollbackError=failure;}
                retry=Time.unscaledTime+1f;revision=uint.MaxValue;
                if(rollbackError!=null)throw new InvalidOperationException("Armor color update failed: "+error.GetBaseException().Message+"; restoring the base appearance also failed: "+rollbackError.GetBaseException().Message,error);
                throw;
            }
        }
        internal void Restore()
        {
            // A resolved pickup/style replacement can have the same revision
            // and material hashes. It must not inherit the previous item's
            // lookup backoff after its visible palette has been restored.
            retry=0;applied=null;Exception failure=null;
            foreach(var binding in bindings)
            {
                if(binding.Key==null)continue;
                string key=binding.Key;binding.Key=null;
                CachedMesh entry;meshes.TryGetValue(key,out entry);
                try
                {
                    if(binding.Renderer!=null)
                    {
                        if(binding.Skin!=null)binding.Skin.sharedMesh=binding.Source;else if(binding.Filter!=null)binding.Filter.sharedMesh=binding.Source;
                        if(entry!=null&&entry.Materials!=null)binding.Renderer.sharedMaterials=binding.OriginalMaterials;
                        binding.Renderer.SetPropertyBlock(binding.Original);
                    }
                }
                catch(Exception error){if(failure==null)failure=error;}
                finally
                {
                    if(entry!=null&&--entry.References==0)
                    {
                        meshes.Remove(key);
                        try{Destroy(entry);}catch(Exception error){if(failure==null)failure=error;}
                    }
                }
            }
            if(failure!=null)throw new InvalidOperationException("Could not restore an armor renderer's base appearance",failure);
        }
        static void Destroy(CachedMesh entry)
        {
            Destroy(entry.Mesh);
            if(entry.OwnedMaterials!=null)foreach(var material in entry.OwnedMaterials)entry.ReleaseMaterial(material);
        }
        static void Destroy(UnityEngine.Object value){if(value==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
        internal static int CachedMeshCount{get{return meshes.Count;}}
        public void Dispose(){if(disposed)return;Restore();bindings.Clear();disposed=true;}
    }

#if !UNITY_EDITOR
    internal static class ArmorMaterialPalette
    {
        static readonly Dictionary<ulong,Color> colors=new Dictionary<ulong,Color>();
        static readonly Dictionary<uint,Color> rawColors=new Dictionary<uint,Color>();
        static readonly Dictionary<uint,Material> woods=new Dictionary<uint,Material>();
        static readonly Dictionary<uint,Material> leathers=new Dictionary<uint,Material>();
        internal static void Clear(){colors.Clear();rawColors.Clear();woods.Clear();leathers.Clear();ArmorLeatherSurface.Clear();}
        internal static Material ResolveLeather(uint hash)
        {
            Material surface;
            if(leathers.TryGetValue(hash,out surface)&&surface!=null)return surface;
            var leather=NativeMaterial(hash);
            if(leather==null)return null;
            surface=leather.GetNonAtlasedNonDeformingMaterial(PhysicalMaterialChannel.A);
            if(surface==null||!surface.HasProperty("_Color2"))surface=leather.GetMaterial(PhysicalMaterialChannel.A);
            if(surface==null||!surface.HasProperty("_Color2"))return null;
            leathers[hash]=surface;
            return surface;
        }
        internal static Material ResolveWood(uint hash)
        {
            Material surface;
            if(woods.TryGetValue(hash,out surface)&&surface!=null)return surface;
            var wood=NativeMaterial(hash);
            if(wood==null)return null;
            surface=wood.GetNonAtlasedNonDeformingMaterial(PhysicalMaterialChannel.A);
            if(surface==null||surface.mainTexture==null)return null;
            woods[hash]=surface;
            return surface;
        }
        internal static Color? Resolve(uint hash,ArmorStyle style)
        {
            if(hash==0)return new Color(1,1,1,0);
            if(style==ArmorStyle.Hebios)style=ArmorStyle.Metal;
            ulong key=((ulong)style<<32)|hash;
            Color result;if(colors.TryGetValue(key,out result))return result;
            var raw=Read(hash);
            var basisMaterial=Alta.HashedGeneralValue<PhysicalMaterial>.Get("iron",false);
            var basis=basisMaterial==null?null:Read(basisMaterial.Hash);
            if(!raw.HasValue||!basis.HasValue)return null;
            result=new Color(Ratio(raw.Value.r,basis.Value.r),Ratio(raw.Value.g,basis.Value.g),Ratio(raw.Value.b,basis.Value.b),1);
            colors[key]=result;return result;
        }
        static float Ratio(float value,float baseline){return Mathf.Clamp(value/Math.Max(baseline,.01f),0,2);}
        static PhysicalMaterial NativeMaterial(uint hash)
        {
            foreach(var candidate in Alta.HashedGeneralValue<PhysicalMaterial>.All)if(candidate!=null&&candidate.Hash==hash)return candidate;
            return null;
        }
        static Color? Read(uint hash)
        {
            Color result;if(rawColors.TryGetValue(hash,out result))return result;
            var material=NativeMaterial(hash);if(material==null)return null;
            var native=material.GetNonAtlasedNonDeformingMaterial(PhysicalMaterialChannel.A);if(native==null)return null;
            var sampled=ArmorMaterialMesh.SampleColdColor(native);if(!sampled.HasValue)return null;result=sampled.Value;rawColors[hash]=result;return result;
        }
    }
#endif
}
