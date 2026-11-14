using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PlayerArmorMod
{
    internal sealed class ArmorDummyDurability : IDisposable
    {
        const float Width = .102f;
        const float Height = .044f;
        const float PixelSize = .0029f;
        static readonly string[] Digits = {
            "01110/10001/10011/10101/11001/10001/01110",
            "00100/01100/00100/00100/00100/00100/01110",
            "01110/10001/00001/00010/00100/01000/11111",
            "11110/00001/00001/01110/00001/00001/11110",
            "00010/00110/01010/10010/11111/00010/00010",
            "11111/10000/10000/11110/00001/10001/01110",
            "00110/01000/10000/11110/10001/10001/01110",
            "11111/00001/00010/00100/01000/01000/01000",
            "01110/10001/10001/01110/10001/10001/01110",
            "01110/10001/10001/01111/00001/00010/01100"
        };
        const string Percent = "11001/11010/00010/00100/01000/01011/10011";
        static readonly Color32 Frame = new Color32(114, 92, 65, 255);
        static readonly Color32 Background = new Color32(32, 28, 25, 255);
        static readonly Color32 Track = new Color32(66, 57, 49, 255);
        static readonly Color32 Text = new Color32(246, 237, 218, 255);
        static readonly Color32 Healthy = new Color32(133, 195, 116, 255);
        static readonly Color32 Worn = new Color32(232, 175, 83, 255);
        static readonly Color32 Low = new Color32(239, 113, 98, 255);
        readonly Badge[] badges = new Badge[ArmorCatalog.SlotCount];
        readonly List<Vector3> vertices = new List<Vector3>(256);
        readonly List<Vector3> normals = new List<Vector3>(256);
        readonly List<Color32> colors = new List<Color32>(256);
        readonly List<int> indices = new List<int>(384);
        GameObject root;
        Material material;
        float nextAttempt;
        bool disposed;

        sealed class Badge
        {
            internal GameObject Object;
            internal Mesh Mesh;
            internal int LastCondition = -1;
        }

        internal void Update(GameObject template, Transform room, Transform chest, Transform viewer, int layer,
            Func<int, Transform> pointForSlot, ArmorActorState state)
        {
            if (disposed || room == null || !room.gameObject.activeInHierarchy ||
                viewer == null || state == null || state.EquippedMask == 0)
            {
                Hide();
                return;
            }

            Vector3 forward = Vector3.ProjectOnPlane(room.forward, Vector3.up).normalized;
            Vector3 towardViewer = Vector3.ProjectOnPlane(viewer.position - room.position, Vector3.up);
            if (forward.sqrMagnitude < .5f) forward = towardViewer.normalized;
            if (Vector3.Dot(forward, towardViewer) < 0) forward = -forward;
            if (forward.sqrMagnitude < .5f)
            {
                Hide();
                return;
            }

            if (root == null && !TryCreate(template)) return;
            Vector3 leftSide = Vector3.Cross(forward, Vector3.up);
            Transform leftShoulder = pointForSlot((int)ArmorSlot.LeftShoulder);
            Transform rightShoulder = pointForSlot((int)ArmorSlot.RightShoulder);
            if (leftShoulder != null && rightShoulder != null &&
                Vector3.Dot(leftShoulder.position - rightShoulder.position, leftSide) < 0)
                leftSide = -leftSide;
            bool anyVisible = false;
            for (int i = 0; i < badges.Length; i++)
            {
                var badge = badges[i];
                ArmorSlot slot = (ArmorSlot)i;
                Transform point = (state.EquippedMask & ArmorCatalog.SlotMask(slot)) != 0 ? pointForSlot(i) : null;
                if (point == null || !point.gameObject.activeInHierarchy)
                {
                    if (badge.Object.activeSelf) badge.Object.SetActive(false);
                    continue;
                }

                Vector3 position = PositionFor(slot, point.position,
                    chest == null ? room.position : chest.position, forward, leftSide);
                Vector3 look = position - viewer.position;
                if (Vector3.Cross(look, Vector3.up).sqrMagnitude < .00001f) look = -forward;
                badge.Object.transform.SetPositionAndRotation(position, Quaternion.LookRotation(look, Vector3.up));
                if (badge.Object.layer != layer) badge.Object.layer = layer;
                int condition = state.Durability[i];
                if (badge.LastCondition != condition)
                {
                    Draw(badge.Mesh, condition);
                    badge.LastCondition = condition;
                }
                if (!badge.Object.activeSelf) badge.Object.SetActive(true);
                anyVisible = true;
            }
            if (root.activeSelf != anyVisible) root.SetActive(anyVisible);
        }

        static Vector3 PositionFor(ArmorSlot slot, Vector3 point, Vector3 chest, Vector3 forward, Vector3 leftSide)
        {
            switch (slot)
            {
                case ArmorSlot.Head:
                    return point + leftSide * .25f + Vector3.up * .11f + forward * .13f;
                case ArmorSlot.Chest:
                    return point + Vector3.up * .10f + forward * .26f;
                case ArmorSlot.Hips:
                    return point - Vector3.up * .06f + forward * .25f;
                default:
                    bool left = slot == ArmorSlot.LeftShoulder || slot == ArmorSlot.LeftForearm;
                    bool shoulder = slot == ArmorSlot.LeftShoulder || slot == ArmorSlot.RightShoulder;
                    float side = left ? 1 : -1;
                    float fromCenter = Vector3.Dot(point - chest, leftSide) * side;
                    float minimum = shoulder ? .39f : .31f;
                    float clearance = shoulder ? .19f : .125f;
                    float outward = Mathf.Max(clearance, minimum - fromCenter);
                    return point + leftSide * (side * outward) + forward * .095f
                        + Vector3.up * (shoulder ? .05f : -.035f);
            }
        }

        bool TryCreate(GameObject template)
        {
            float now = Time.unscaledTime;
            if (now < nextAttempt) return false;
            // Scene changes can destroy the root before releasing the owned assets.
            Release();
            var source = ArmorConnectionGuide.FindSource(template);
            if (source == null)
            {
                nextAttempt = now + 2;
                return false;
            }

            try
            {
                material = new Material(source) { name = "PlayerArmor durability", hideFlags = HideFlags.DontSave };
                material.SetColor("_Color", Color.white);
                material.SetTexture("_MainTex", Texture2D.whiteTexture);
                material.SetFloat("_UseAtlas", 0);
                material.SetFloat("_UseVertexColor", 1);
                material.SetFloat("_Cutoff", 0);
                material.SetFloat("_Glow", .65f);
                ArmorStereo.Register(material);
                root = new GameObject("PlayerArmor dummy durability") { hideFlags = HideFlags.DontSave };
                root.SetActive(false);
                for (int i = 0; i < badges.Length; i++)
                {
                    var plate = new GameObject(((ArmorSlot)i).ToString()) { hideFlags = HideFlags.DontSave };
                    plate.transform.SetParent(root.transform, false);
                    plate.SetActive(false);
                    var mesh = new Mesh { name = plate.name + " durability", hideFlags = HideFlags.DontSave };
                    mesh.MarkDynamic();
                    badges[i] = new Badge { Object = plate, Mesh = mesh };
                    plate.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = plate.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }
                return true;
            }
            catch (Exception error)
            {
                Release();
                nextAttempt = now + 10;
                Core.Error("PLAYER_ARMOR_DURABILITY_DISPLAY_DEFERRED " + error.GetBaseException().Message);
                return false;
            }
        }

        void Draw(Mesh mesh, int condition)
        {
            vertices.Clear();
            normals.Clear();
            colors.Clear();
            indices.Clear();
            Rectangle(-Width / 2, -Height / 2, Width / 2, Height / 2, 0, Frame);
            Rectangle(-.049f, -.020f, .049f, .020f, -.0003f, Background);
            Rectangle(-.042f, -.015f, .042f, -.009f, -.0006f, Track);
            Color32 tint = condition > 500 ? Healthy : condition > 200 ? Worn : Low;
            if (condition > 0)
            {
                float fill = Mathf.Max(.001f, .084f * condition / 1000);
                Rectangle(-.042f, -.015f, -.042f + fill, -.009f, -.0009f, tint);
            }

            int whole = condition == 0 ? 0 : condition == 1000 ? 100 : Mathf.Clamp((condition + 5) / 10, 1, 99);
            string label = whole.ToString(CultureInfo.InvariantCulture) + "%";
            float left = -(label.Length * 6 - 1) * PixelSize / 2;
            foreach (char character in label)
            {
                DrawGlyph(character == '%' ? Percent : Digits[character - '0'], left, condition == 0 ? Low : Text);
                left += 6 * PixelSize;
            }
            mesh.Clear(false);
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0);
        }

        void DrawGlyph(string pattern, float left, Color32 color)
        {
            for (int row = 0; row < 7; row++)
            {
                float top = .016f - row * PixelSize;
                int column = 0;
                while (column < 5)
                {
                    if (pattern[row * 6 + column] != '1')
                    {
                        column++;
                        continue;
                    }
                    int start = column++;
                    while (column < 5 && pattern[row * 6 + column] == '1') column++;
                    Rectangle(left + start * PixelSize, top - PixelSize,
                        left + column * PixelSize, top, -.0009f, color);
                }
            }
        }

        void Rectangle(float left, float bottom, float right, float top, float depth, Color32 color)
        {
            int first = vertices.Count;
            vertices.Add(new Vector3(left, bottom, depth));
            vertices.Add(new Vector3(left, top, depth));
            vertices.Add(new Vector3(right, top, depth));
            vertices.Add(new Vector3(right, bottom, depth));
            for (int i = 0; i < 4; i++)
            {
                normals.Add(Vector3.back);
                colors.Add(color);
            }
            indices.Add(first);
            indices.Add(first + 1);
            indices.Add(first + 2);
            indices.Add(first);
            indices.Add(first + 2);
            indices.Add(first + 3);
        }

        internal void Hide()
        {
            if (root != null && root.activeSelf) root.SetActive(false);
        }

        void Release()
        {
            Hide();
            if (root != null) Object.Destroy(root);
            root = null;
            foreach (var badge in badges)
                if (badge != null && badge.Mesh != null) Object.Destroy(badge.Mesh);
            Array.Clear(badges, 0, badges.Length);
            if (material == null) return;
            ArmorStereo.Unregister(material);
            Object.Destroy(material);
            material = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Release();
        }
    }
}
