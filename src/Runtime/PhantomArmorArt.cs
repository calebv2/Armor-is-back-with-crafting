using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PlayerArmorMod
{
    // Authored meshes are loaded once. Wearable frames use the existing native
    // bone binding; loose items use a single static mesh and native pickup.
    internal sealed class PhantomArmorArt : IDisposable
    {
        sealed class Document { public int version { get; set; } public string[] colors { get; set; } public Part[] parts { get; set; } }
        sealed class Part { public string slot { get; set; } public Geometry[] groups { get; set; } public Geometry[] pickup { get; set; } }
        sealed class Skin
        {
            public string[] bones { get; set; }
            public float[] bindposes { get; set; }
            public int[] indices { get; set; }
            public float[] weights { get; set; }

            public BoneWeight Weight(int vertex)
            {
                int start = vertex * 4;
                return new BoneWeight {
                    boneIndex0 = indices[start], boneIndex1 = indices[start + 1],
                    boneIndex2 = indices[start + 2], boneIndex3 = indices[start + 3],
                    weight0 = weights[start], weight1 = weights[start + 1],
                    weight2 = weights[start + 2], weight3 = weights[start + 3]
                };
            }
        }
        sealed class Geometry
        {
            public string bone { get; set; }
            public string surface { get; set; }
            public Skin skin { get; set; }
            public float[] vertices { get; set; }
            public float[] normals { get; set; }
            public float[] tints { get; set; }
            public float[] jadeEdges { get; set; }
            public float[] textureUV { get; set; }
            public int[] projections { get; set; }
            public int[] triangles { get; set; }
            public int[] materials { get; set; }
        }
        readonly List<Object> owned = new List<Object>();
        internal readonly Pose[] StandPoses = new Pose[ArmorCatalog.SlotCount];
        internal GameObject Template { get; private set; }

        internal PhantomArmorArt(GameObject shaderSource, GameObject glossSource)
        {
            Template = new GameObject("ATT_PhantomArmor_Visuals");
            Template.SetActive(false);
            Object.DontDestroyOnLoad(Template);
            try
            {
                Document data;
                using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("PlayerArmor.Phantom"))
                using (var zipped = new GZipStream(source ?? throw new InvalidDataException("Missing Phantom meshes"), CompressionMode.Decompress))
                using (var reader = new StreamReader(zipped))
                    data = JsonConvert.DeserializeObject<Document>(reader.ReadToEnd());
                if (data == null || data.version != 1 || data.parts.Length != 7)
                    throw new InvalidDataException("Invalid Phantom mesh document");
                var shader = shaderSource.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(r => r.sharedMaterials).Where(m => m != null)
                    .Select(m => m.shader).First(s => s != null && s.name == "PlayerArmor/Lit");
                var palette = data.colors.Select(hex =>
                {
                    if (!ColorUtility.TryParseHtmlString("#" + hex, out var color))
                        throw new InvalidDataException("Invalid Phantom palette");
                    // Material colors are converted by Unity in a linear project;
                    // mesh vertex colors need the same conversion explicitly.
                    return QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
                }).ToArray();
                var material = new Material(shader) { name = "Phantom material", color = Color.white, hideFlags = HideFlags.DontSave };
                material.SetTexture("_MainTex", Core.IsServer ? Texture2D.whiteTexture : LoadAtlas());
                material.SetFloat("_UseAtlas", 1);
                material.SetFloat("_UseVertexColor", 1);
                // The bright jade vertex edges retain a little color in darkness.
                // Black surfaces are still multiplied by their dark vertex tint.
                material.SetFloat("_Glow", .18f);
                owned.Add(material);
                var glossShader = glossSource.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(r => r.sharedMaterials).Where(m => m != null)
                    .Select(m => m.shader).First(s => s != null && s.name == "PlayerArmor/WyrmLit");
                var orbMaterial = new Material(glossShader)
                    { name = "Phantom material Guard orb", color = new Color(.031f,.091f,.057f,1), hideFlags = HideFlags.DontSave };
                owned.Add(orbMaterial);
                orbMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
                orbMaterial.SetTexture("_OcclusionMap", Texture2D.whiteTexture);
                orbMaterial.SetTexture("_MetallicGlossMap", Texture2D.whiteTexture);
                orbMaterial.SetFloat("_Metallic", .22f);
                orbMaterial.SetFloat("_Glossiness", .864f);
                var flatNormal = new Texture2D(2,2,TextureFormat.RGBA32,false,true) { name = "Phantom orb smooth normal", hideFlags = HideFlags.DontSave };
                owned.Add(flatNormal);
                // Unity's desktop normal unpacker multiplies red by alpha;
                // (1,.5,1,.5) decodes to a flat (0,0,1) tangent-space normal.
                flatNormal.SetPixels(Enumerable.Repeat(new Color(1,.5f,1,.5f),4).ToArray());
                flatNormal.Apply(false,true);
                orbMaterial.SetTexture("_BumpMap",flatNormal);
                var worn = Child(Template.transform, "Phantom");
                var loose = Child(Child(Template.transform, "Pickups"), "Phantom");
                foreach (var part in data.parts)
                {
                    var slot = (ArmorSlot)Enum.Parse(typeof(ArmorSlot), part.slot);
                    // The first shell vertex is exported twice: in the bone's
                    // local frame for wearing, and in centered rig axes for pickup.
                    // The same rigid transform fits the whole piece, including gloves.
                    var intoBone = Quaternion.Inverse(ArmorCraftingMesh.WorkbenchRotation(slot));
                    var wornShell = part.groups[0].vertices;
                    var looseShell = part.pickup[0].vertices;
                    var offset = new Vector3(wornShell[0], wornShell[1], wornShell[2])
                        - intoBone * new Vector3(looseShell[0], looseShell[1], looseShell[2]);
                    StandPoses[(int)slot] = new Pose(offset, intoBone);
                    var root = Child(worn, "Armor_" + part.slot);
                    foreach (var group in part.groups)
                    {
                        var target = group.skin == null ? Child(Child(root, "ATTBone." + group.bone), SurfaceName(group.surface))
                            : Child(root, "Glove");
                        Render(target, new[] { group }, palette, group.surface == null ? material : orbMaterial);
                    }
                    var pickup = Child(loose, "Armor_" + part.slot);
                    var surfaces = part.pickup.GroupBy(g => g.surface).ToArray();
                    if (surfaces.Length == 1) Render(pickup,part.pickup,palette,material);
                    else
                    {
                        foreach (var surface in surfaces)
                            Render(Child(pickup, SurfaceName(surface.Key)),surface.ToArray(),palette,surface.Key == null ? material : orbMaterial);
                        // Native loose pickups require one static mesh. Preserve
                        // the glossy surface as a submesh, with no extra dock/collider.
                        var filters = pickup.GetComponentsInChildren<MeshFilter>(true);
                        var mesh = new Mesh { name = "Phantom " + part.slot + " pickup", hideFlags = HideFlags.DontSave };
                        if (filters.Sum(f => f.sharedMesh.vertexCount) > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                        mesh.CombineMeshes(filters.Select(f => new CombineInstance { mesh = f.sharedMesh,
                            transform = pickup.worldToLocalMatrix * f.transform.localToWorldMatrix }).ToArray(),false,true);
                        owned.Add(mesh);
                        var pickupMaterials = filters.Select(f => f.GetComponent<MeshRenderer>().sharedMaterial).ToArray();
                        foreach (var filter in filters) Object.DestroyImmediate(filter.gameObject);
                        pickup.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                        pickup.gameObject.AddComponent<MeshRenderer>().sharedMaterials = pickupMaterials;
                    }
                }
                if (!Core.IsServer) ArmorStereo.Configure(Template);
            }
            catch { Dispose(); throw; }
        }

        static string SurfaceName(string surface) => surface == "orb" ? "GuardOrb" : surface == "eyes" ? "Eyes" : "Shell";

        static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        Texture2D LoadAtlas()
        {
            using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("PlayerArmor.Phantom.Atlas"))
            using (var output = new MemoryStream())
            {
                if (source == null) throw new InvalidDataException("Missing Phantom material atlas");
                source.CopyTo(output);
                var texture = new Texture2D(2, 2, TextureFormat.RGB24, true)
                    { name = "Phantom shell cloth leather bronze atlas", anisoLevel = 4, wrapMode = TextureWrapMode.Clamp };
                owned.Add(texture);
                if (!texture.LoadImage(output.ToArray(), true)) throw new InvalidDataException("Invalid Phantom atlas");
                return texture;
            }
        }

        void Render(Transform target, Geometry[] groups, Color[] palette, Material material)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var vertexTints = new List<float>();
            var jadeEdges = new List<float>();
            var projectedUV = new List<Vector2>();
            var faces = palette.Select(_ => new List<int>()).ToArray();
            foreach (var group in groups)
            {
                int offset = vertices.Count;
                if (group.vertices.Length % 3 != 0 || group.normals.Length != group.vertices.Length ||
                    group.triangles.Length != group.materials.Length * 3 || group.tints.Length != group.vertices.Length / 3 ||
                    (group.jadeEdges != null && group.jadeEdges.Length != group.tints.Length) ||
                    group.textureUV.Length != group.vertices.Length * 2 || group.projections.Length != group.materials.Length)
                    throw new InvalidDataException("Invalid Phantom mesh buffers");
                for (int i = 0; i < group.vertices.Length; i += 3)
                {
                    var v = new Vector3(group.vertices[i], group.vertices[i + 1], group.vertices[i + 2]);
                    if (float.IsNaN(v.sqrMagnitude) || float.IsInfinity(v.sqrMagnitude))
                        throw new InvalidDataException("Non-finite Phantom vertex");
                    vertices.Add(v);
                    normals.Add(new Vector3(group.normals[i], group.normals[i + 1], group.normals[i + 2]));
                    vertexTints.Add(group.tints[i / 3]);
                    jadeEdges.Add(group.jadeEdges == null ? 0 : group.jadeEdges[i / 3]);
                    for (int axis = 0; axis < 3; axis++)
                    {
                        int uvIndex = i * 2 + axis * 2;
                        projectedUV.Add(new Vector2(group.textureUV[uvIndex], group.textureUV[uvIndex + 1]));
                    }
                }
                for (int t = 0; t < group.materials.Length; t++)
                    for (int j = 0; j < 3; j++)
                    {
                        int index = group.triangles[t * 3 + j];
                        if (index < 0 || index >= group.vertices.Length / 3) throw new InvalidDataException("Invalid Phantom index");
                        faces[group.materials[t]].Add((offset + index) * 3 + group.projections[t]);
                    }
            }
            // Palette boundaries share one opaque stereo material. This avoids
            // a separate draw call for each strap, rivet, lining and shell.
            var combinedVertices = new List<Vector3>();
            var combinedNormals = new List<Vector3>();
            var combinedColors = new List<Color>();
            var combinedTriangles = new List<int>();
            var combinedUV = new List<Vector2>();
            var combinedAtlas = new List<Vector4>();
            var skin = groups.Length == 1 ? groups[0].skin : null;
            var weights = skin == null ? null : new List<BoneWeight>();
            for (int color = 0; color < faces.Length; color++)
            {
                var mapped = new Dictionary<int, int>();
                foreach (int projectedIndex in faces[color])
                {
                    int index = projectedIndex / 3;
                    if (!mapped.TryGetValue(projectedIndex, out int destination))
                    {
                        destination = combinedVertices.Count;
                        mapped.Add(projectedIndex, destination);
                        combinedVertices.Add(vertices[index]);
                        combinedNormals.Add(normals[index]);
                        if (skin != null) weights.Add(skin.Weight(index));
                        var tint = Color.Lerp(palette[color], palette[8], jadeEdges[index]) * (vertexTints[index] * 2.2f);
                        tint.a = 1;
                        combinedColors.Add(tint);
                        bool cloth = color == 3 || color == 4 || color == 9 || color == 10;
                        bool leather = color == 5, bronze = color == 6;
                        combinedUV.Add(color == 7 || color == 8 ? new Vector2(.46f, .46f) :
                            projectedUV[projectedIndex] * (cloth ? 3f : leather || bronze ? 1f : .6f));
                        combinedAtlas.Add(new Vector4(cloth || bronze ? .506f : .006f,
                            leather || bronze ? .006f : .506f, .488f, .488f));
                    }
                    combinedTriangles.Add(destination);
                }
            }
            var mesh = new Mesh { name = "Phantom " + target.parent.name + " " + target.name, hideFlags = HideFlags.DontSave };
            owned.Add(mesh);
            if (combinedVertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(combinedVertices);
            mesh.SetNormals(combinedNormals);
            mesh.SetColors(combinedColors);
            mesh.SetUVs(0, combinedUV);
            mesh.SetUVs(3, combinedAtlas);
            mesh.SetTriangles(combinedTriangles, 0);
            if (material.shader.name == "PlayerArmor/WyrmLit") mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            if (skin == null)
            {
                target.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                target.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            }
            else
            {
                var rest = Child(target.parent, "OriginalSourceBones");
                var bones = new Transform[skin.bones.Length];
                var bindposes = new Matrix4x4[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                {
                    var bind = new Matrix4x4();
                    for (int j = 0; j < 16; j++) bind[j / 4, j % 4] = skin.bindposes[16 * i + j];
                    bindposes[i] = bind;
                    var pose = bind.inverse;
                    var bone = bones[i] = Child(rest, skin.bones[i]);
                    bone.localPosition = pose.GetColumn(3);
                    bone.localRotation = pose.rotation;
                    bone.localScale = pose.lossyScale;
                }
                mesh.bindposes = bindposes;
                mesh.boneWeights = weights.ToArray();
                var renderer = target.gameObject.AddComponent<SkinnedMeshRenderer>();
                renderer.bones = bones;
                renderer.rootBone = rest;
                renderer.sharedMesh = mesh;
                renderer.sharedMaterial = material;
                renderer.quality = SkinQuality.Bone4;
                renderer.updateWhenOffscreen = true;
            }
        }

        public void Dispose()
        {
            if (Template != null) Object.Destroy(Template);
            foreach (var asset in owned)
            {
                if (asset is Material material) ArmorStereo.Unregister(material);
                if (asset != null) Object.Destroy(asset);
            }
            owned.Clear();
        }
    }
}
