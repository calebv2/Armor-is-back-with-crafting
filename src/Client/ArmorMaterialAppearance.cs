using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace PlayerArmorMod
{
    // Own only the per-instance mesh references/property blocks. Source meshes,
    // source materials, skeletons, colliders and renderer visibility are untouched.
    internal sealed class ArmorMaterialAppearance:IDisposable
    {
        sealed class CachedMesh {internal Mesh Mesh;internal int References;}
        sealed class Binding
        {
            internal Renderer Renderer;internal MeshFilter Filter;internal SkinnedMeshRenderer Skin;
            internal Mesh Source;internal MaterialPropertyBlock Original;internal string Key;
        }
        static readonly Dictionary<string,CachedMesh> meshes=new Dictionary<string,CachedMesh>();
        readonly List<Binding> bindings=new List<Binding>();
        readonly Func<uint,ArmorStyle,Color?> resolve;
        readonly ArmorStyle style;
        static readonly Dictionary<Material,Color> hideSurfaces=new Dictionary<Material,Color>();
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
                bindings.Add(new Binding{Renderer=renderer,Filter=filter,Skin=skin,Source=source,Original=original});
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
            var palette=new Color[hashes.Count];
            if(palette.Length>32){Restore();return;}
            for(int i=0;i<palette.Length;i++)
            {
                ArmorStyle family=style==ArmorStyle.Hebios&&layoutVersion==2&&i>=primaryCount?ArmorStyle.Leather:style;
                Color? color;try{color=resolve(hashes[i],family);}catch{return;}
                if(!color.HasValue)return;palette[i]=color.Value;
            }
            Apply(palette,layoutVersion,primaryCount,tintFasteners);revision=profileRevision;
        }
        static bool SameHashes(IList<uint> source,uint[] target)
        {if(source==null)return target.Length==0;if(source.Count!=target.Length)return false;for(int i=0;i<target.Length;i++)if(source[i]!=target[i])return false;return true;}
        internal void Apply(Color[] palette,uint layoutVersion=1,int primaryCount=0,bool tintFasteners=true)
        {
            if(disposed)return;
            try
            {
                if(palette==null||palette.Length<1||palette.Length>32)throw new ArgumentException("Armor color palette must contain one to thirty-two colors","palette");
                var signature=new StringBuilder().Append(style).Append(':').Append(layoutVersion).Append(':').Append(primaryCount).Append(':').Append(tintFasteners).Append(':');foreach(var color in palette)signature.Append(color.r.ToString("R",CultureInfo.InvariantCulture)).Append(',').Append(color.g.ToString("R",CultureInfo.InvariantCulture)).Append(',').Append(color.b.ToString("R",CultureInfo.InvariantCulture)).Append(',').Append(color.a.ToString("R",CultureInfo.InvariantCulture)).Append(';');
                string next=signature.ToString();if(applied==next)return;
                Restore();
                foreach(var binding in bindings)
                {
                    if(binding.Renderer==null||binding.Source==null)continue;
                    if(layoutVersion==2?!ArmorSemanticLayout.HasMetadata(binding.Source):!ArmorMaterialMesh.HasCoordinates(binding.Source))continue;
                    var hide=style==ArmorStyle.Leather||style==ArmorStyle.Hebios?GetHideSurfaces(binding.Renderer,binding.Source.subMeshCount):null;
                    string key=binding.Source.GetInstanceID().ToString(CultureInfo.InvariantCulture)+":"+next;
                    if(hide!=null)foreach(var material in binding.Renderer.sharedMaterials)key+=":"+(material==null?0:material.GetInstanceID()).ToString(CultureInfo.InvariantCulture);
                    CachedMesh entry;
                    if(!meshes.TryGetValue(key,out entry))
                    {
                        var built=ArmorMaterialMesh.Build(binding.Source,palette,layoutVersion,primaryCount,tintFasteners,style,hide);
                        entry=new CachedMesh{Mesh=built};
                        try{meshes.Add(key,entry);}catch{Destroy(built);throw;}
                    }
                    entry.References++;binding.Key=key;
                    if(binding.Skin!=null)binding.Skin.sharedMesh=entry.Mesh;else binding.Filter.sharedMesh=entry.Mesh;
                    var block=new MaterialPropertyBlock();binding.Renderer.GetPropertyBlock(block);block.SetFloat("_UseVertexColor",1);block.SetFloat("_Glow",0);binding.Renderer.SetPropertyBlock(block);
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
        static Color?[] GetHideSurfaces(Renderer renderer,int count)
        {
            var materials=renderer.sharedMaterials;
            var result=new Color?[count];
            for(int i=0;i<count&&i<materials.Length;i++)
            {
                var material=materials[i];
                var texture=material==null?null:material.mainTexture;
                if(texture==null||(texture.name!="leather_armor_d"&&texture.name!="ATT_Leather_Albedo"))continue;
                Color sample;
                if(!hideSurfaces.TryGetValue(material,out sample))
                {
                    sample=ArmorMaterialMesh.SampleColdColor(material)??new Color(.45f,.23f,.10f);
                    hideSurfaces[material]=sample;
                }
                result[i]=sample;
            }
            return result;
        }
        internal static void ClearSurfaceColors(){hideSurfaces.Clear();}
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
                try
                {
                    if(binding.Renderer!=null){if(binding.Skin!=null)binding.Skin.sharedMesh=binding.Source;else if(binding.Filter!=null)binding.Filter.sharedMesh=binding.Source;binding.Renderer.SetPropertyBlock(binding.Original);}
                }
                catch(Exception error){if(failure==null)failure=error;}
                finally
                {
                    CachedMesh entry;
                    if(meshes.TryGetValue(key,out entry)&&--entry.References==0)
                    {
                        meshes.Remove(key);
                        try{Destroy(entry.Mesh);}catch(Exception error){if(failure==null)failure=error;}
                    }
                }
            }
            if(failure!=null)throw new InvalidOperationException("Could not restore an armor renderer's base appearance",failure);
        }
        static void Destroy(UnityEngine.Object value){if(value==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
        internal static int CachedMeshCount{get{return meshes.Count;}}
        public void Dispose(){if(disposed)return;Restore();bindings.Clear();disposed=true;}
    }

#if !UNITY_EDITOR
    internal static class ArmorMaterialPalette
    {
        const uint PlainLeather=33384;
        static readonly Dictionary<ulong,Color> colors=new Dictionary<ulong,Color>();
        static readonly Dictionary<uint,Color> rawColors=new Dictionary<uint,Color>();
        internal static void Clear(){colors.Clear();rawColors.Clear();ArmorMaterialAppearance.ClearSurfaceColors();}
        internal static Color? Resolve(uint hash,ArmorStyle style)
        {
            if(hash==0||style==ArmorStyle.Leather&&hash==PlainLeather)return new Color(1,1,1,0);
            if(style==ArmorStyle.Hebios)style=ArmorStyle.Metal;
            ulong key=((ulong)style<<32)|hash;
            Color result;if(colors.TryGetValue(key,out result))return result;
            if(style==ArmorStyle.Leather)
            {
                var native=NativeMaterial(hash);
                var leather=native==null?null:native.GetMaterial(PhysicalMaterialChannel.A);
                if(leather==null||!leather.HasProperty("_Color2"))return null;
                var dye=ArmorMaterialMesh.ShaderColor(leather,"_Color2");
                var plain=NativeMaterial(PlainLeather);
                var reference=plain==null?null:plain.GetMaterial(PhysicalMaterialChannel.A);
                if(reference==null||!reference.HasProperty("_Color2"))return null;
                float brightness=ArmorMaterialMesh.Luminance(ArmorMaterialMesh.ShaderColor(reference,"_Color2"));
                result=new Color(dye.r,dye.g,dye.b,Math.Max(brightness,.01f));
            }
            else
            {
                var raw=Read(hash);
                var basisMaterial=Alta.HashedGeneralValue<PhysicalMaterial>.Get(style==ArmorStyle.Woodland?"oak":"iron",false);
                var basis=basisMaterial==null?null:Read(basisMaterial.Hash);
                if(!raw.HasValue||!basis.HasValue)return null;
                result=new Color(Ratio(raw.Value.r,basis.Value.r),Ratio(raw.Value.g,basis.Value.g),Ratio(raw.Value.b,basis.Value.b),1);
            }
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
