using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using EFT;
using EFT.InventoryLogic;
using EFT.Visual;
using SPT.Reflection.Patching;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodyCamera
{
    internal static class BodyCamBodyView
    {
        internal static ConfigEntry<bool> CfgTorso;
        internal static ConfigEntry<bool> CfgArmor;
        internal static ConfigEntry<bool> CfgHead;
        internal static ConfigEntry<bool> CfgHelmet;
        internal static ConfigEntry<bool> CfgBackpack;
        internal static ConfigEntry<bool> CfgWeapons;

        internal static ConfigEntry<bool> CfgFpTorso;
        internal static ConfigEntry<bool> CfgFpArmor;
        internal static ConfigEntry<bool> CfgFpBackpack;
        internal static ConfigEntry<bool> CfgFpWeapons;
        private static bool _third = true;

        private enum Group
        {
            Torso,
            Armor,
            Head,
            Helmet,
            Backpack,
            Weapons
        }

        private const int GroupCount = 6;

        private sealed class Copy
        {
            public Renderer Source;
            public Renderer Clone;
            public Group Group;
            public bool Skinned;
        }

        private const float SyncInterval = 0.25f;
        private const float CollapseScale = 0.0001f;

        private static readonly List<Copy> _copies = new List<Copy>();
        private static readonly List<Renderer> _tmpRenderers = new List<Renderer>();
        private static object _handsItem;
        private static bool _loggedWeapons;
        private static readonly Dictionary<LODGroup, HashSet<Renderer>> _lod0 =
            new Dictionary<LODGroup, HashSet<Renderer>>();
        private static readonly List<KeyValuePair<Renderer, Group>> _wanted =
            new List<KeyValuePair<Renderer, Group>>();
        private static readonly List<Renderer> _handsRenderers = new List<Renderer>();
        private static readonly List<Renderer> _hiddenHands = new List<Renderer>();
        private static readonly Dictionary<Transform, Transform> _points =
            new Dictionary<Transform, Transform>();

        private static Player _player;
        private static GameObject _root;
        private static MaterialPropertyBlock _mpb;
        private static int _signature;
        private static bool _built;
        private static bool _dirty;
        private static float _nextSync;
        private static bool _hasTorsoCopy;
        private static readonly List<Texture2D> _fullTextures = new List<Texture2D>();
        private static bool _loggedTextures;
        private static bool _failed;

        internal static void BindConfig()
        {
            CfgTorso = ConfigMigration.Bind(
                Plugin.SecBody, "Show Torso", true,
                "Third person: your torso and legs with their own arms are seen.",
                null, 100, null, null);

            CfgArmor = ConfigMigration.Bind(
                Plugin.SecBody, "Show Armor", true,
                "Third person: body armor and the chest rig are seen.",
                null, 90, null, null);

            CfgHead = ConfigMigration.Bind(
                Plugin.SecBody, "Show Head", true,
                "Third person: your head is seen.",
                null, 85, null, null);

            CfgHelmet = ConfigMigration.Bind(
                Plugin.SecBody, "Show Helmet", true,
                "Third person: helmet, headset, glasses and face cover are seen.",
                null, 80, null, null);

            CfgBackpack = ConfigMigration.Bind(
                Plugin.SecBody, "Show Backpack", true,
                "Third person: your backpack is seen.",
                null, 75, null, null);

            CfgWeapons = ConfigMigration.Bind(
                Plugin.SecBody, "Show Weapons", true,
                "Third person: weapons on the back and the pistol in the holster are seen.",
                null, 70, null, null);

            CfgFpTorso = ConfigMigration.Bind(
                Plugin.SecBody, "First Person Torso", false,
                "First person: your torso with its own arms (game hands hidden).",
                null, 45, null, null);

            CfgFpArmor = ConfigMigration.Bind(
                Plugin.SecBody, "First Person Armor", false,
                "First person: body armor and the chest rig are seen.",
                null, 44, null, null);

            CfgFpBackpack = ConfigMigration.Bind(
                Plugin.SecBody, "First Person Backpack", false,
                "First person: your backpack is seen.",
                null, 43, null, null);

            CfgFpWeapons = ConfigMigration.Bind(
                Plugin.SecBody, "First Person Weapons", false,
                "First person: weapons on the back and the pistol in the holster are seen.",
                null, 42, null, null);
        }

        private static bool AnyOn()
        {
            return ShowTorso() || ShowArmor() || ShowHead() || ShowHelmet() || ShowBackpack() || ShowWeapons();
        }

        private static bool ShowTorso() { return _third ? On(CfgTorso) : On(CfgFpTorso); }
        private static bool ShowArmor() { return _third ? On(CfgArmor) : On(CfgFpArmor); }
        private static bool ShowHead() { return _third && On(CfgHead); }
        private static bool ShowHelmet() { return _third && On(CfgHelmet); }
        private static bool ShowBackpack() { return _third ? On(CfgBackpack) : On(CfgFpBackpack); }
        private static bool ShowWeapons() { return _third ? On(CfgWeapons) : On(CfgFpWeapons); }

        internal static void Tick(Player player, bool thirdPerson)
        {
            float now = Time.unscaledTime;

            try
            {
                if (thirdPerson != _third)
                {
                    _third = thirdPerson;
                    _dirty = true;
                }

                if (_failed || !AnyOn() || player == null ||
                    player.PointOfView != EPointOfView.FirstPerson)
                {
                    if (_built || _copies.Count > 0 || _hiddenHands.Count > 0)
                        Clear();

                    return;
                }

                if (player != _player)
                {
                    Clear();
                    _player = player;
                }

                if (!_dirty && _built)
                    _dirty = SourcesChanged();

                object hands = HandsItem(player);

                if (!ReferenceEquals(hands, _handsItem))
                {
                    _handsItem = hands;
                    _dirty = true;
                }

                if (_dirty || now >= _nextSync)
                {
                    _nextSync = now + SyncInterval;
                    _dirty = false;
                    Sync(player);
                }

                UpdateHands();
            }
            catch (Exception ex)
            {
                _failed = true;
                Plugin.Log.LogWarning("[BodyCamera] Body view off after an error: " + ex);

                try
                {
                    Clear();
                }
                catch
                {
                }
            }
        }

        private static bool On(ConfigEntry<bool> e)
        {
            return e != null && e.Value;
        }

        internal static void Clear()
        {
            RestoreHands();
            ReleaseTextures();

            for (int i = 0; i < _copies.Count; i++)
            {
                Renderer c = _copies[i].Clone;

                if (c != null)
                    UnityEngine.Object.Destroy(c.gameObject);
            }

            _copies.Clear();
            _lod0.Clear();
            _handsRenderers.Clear();

            foreach (var pair in _points)
            {
                if (pair.Value != null)
                    UnityEngine.Object.Destroy(pair.Value.gameObject);
            }

            _points.Clear();

            if (_root != null)
                UnityEngine.Object.Destroy(_root);

            _root = null;
            _player = null;
            _built = false;
            _dirty = false;
            _signature = 0;
            _nextSync = 0f;
            _hasTorsoCopy = false;
            _handsItem = null;
        }

        internal static void ResetForRaid()
        {
            Clear();
            _failed = false;
            _loggedTextures = false;
            _loggedWeapons = false;
        }

        private static bool SourcesChanged()
        {
            for (int i = 0; i < _copies.Count; i++)
            {
                Copy c = _copies[i];

                if (c.Source == null || c.Clone == null)
                    return true;

                if (c.Group == Group.Weapons
                        ? !c.Source.gameObject.activeInHierarchy
                        : !Usable(c.Source))
                    return true;

                if (c.Skinned &&
                    ((SkinnedMeshRenderer)c.Source).sharedMesh != ((SkinnedMeshRenderer)c.Clone).sharedMesh)
                    return true;
            }

            return false;
        }

        private static bool Usable(Renderer r)
        {
            return r != null && r.enabled && r.gameObject.activeInHierarchy &&
                   (r is SkinnedMeshRenderer || r is MeshRenderer);
        }

        private static void Sync(Player player)
        {
            PlayerBody body = player.PlayerBody;

            if (body == null)
                return;

            CollectWanted(body);

            int sig = (_handsItem != null ? _handsItem.GetHashCode() : 0) + 17 + (ShowTorso() ? 2 : 0) + (ShowArmor() ? 4 : 0) +
                      (ShowHead() ? 8 : 0) + (ShowHelmet() ? 16 : 0) + (ShowBackpack() ? 32 : 0) +
                      (ShowWeapons() ? 64 : 0) + (_third ? 128 : 0);

            for (int i = 0; i < _wanted.Count; i++)
            {
                Renderer r = _wanted[i].Key;
                sig = sig * 31 + r.GetInstanceID();
                sig = sig * 31 + (int)_wanted[i].Value;

                var smr = r as SkinnedMeshRenderer;

                if (smr != null && smr.sharedMesh != null)
                    sig = sig * 31 + smr.sharedMesh.GetInstanceID();
            }

            if (_built && sig == _signature)
            {
                RefreshBlocks();
                return;
            }

            Rebuild(body);
            _signature = sig;
            _built = true;
        }

        private static void CollectWanted(PlayerBody body)
        {
            _wanted.Clear();

            if (ShowTorso())
            {
                AddSkin(body, EBodyModelPart.Body, Group.Torso, false);
                AddSkin(body, EBodyModelPart.Feet, Group.Torso, true);
            }

            if (ShowArmor())
            {
                AddSlot(body, EquipmentSlot.ArmorVest, Group.Armor);
                AddSlot(body, EquipmentSlot.TacticalVest, Group.Armor);
            }

            if (ShowHead())
                AddSkin(body, EBodyModelPart.Head, Group.Head, false);

            if (ShowHelmet())
            {
                AddSlot(body, EquipmentSlot.Headwear, Group.Helmet);
                AddSlot(body, EquipmentSlot.Earpiece, Group.Helmet);
                AddSlot(body, EquipmentSlot.Eyewear, Group.Helmet);
                AddSlot(body, EquipmentSlot.FaceCover, Group.Helmet);
            }

            if (ShowBackpack())
                AddSlot(body, EquipmentSlot.Backpack, Group.Backpack);

            if (ShowWeapons())
            {
                bool log = !_loggedWeapons;
                _loggedWeapons = true;
                AddWeaponSlot(body, EquipmentSlot.FirstPrimaryWeapon, log);
                AddWeaponSlot(body, EquipmentSlot.SecondPrimaryWeapon, log);
                AddWeaponSlot(body, EquipmentSlot.Holster, log);
            }
        }

        private static void AddSkin(PlayerBody body, EBodyModelPart part, Group group, bool shadowOnly)
        {
            if (body.BodySkins == null)
                return;

            LoddedSkin skin;

            if (!body.BodySkins.TryGetValue(part, out skin) || skin == null)
                return;

            foreach (Renderer r in skin.GetRenderers())
            {
                if (Usable(r) && r is SkinnedMeshRenderer)
                {
                    if (shadowOnly && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly)
                        return;

                    AddWanted(r, group);
                    return;
                }
            }
        }

        private static void AddSlot(PlayerBody body, EquipmentSlot slot, Group group)
        {
            if (body.SlotViews == null)
                return;

            PlayerBody.SlotView view;

            if (!body.SlotViews.TryGetByKey(slot, out view) || view == null)
                return;

            Renderer[] list = view.Renderers;

            if (list == null)
                return;

            for (int i = 0; i < list.Length; i++)
            {
                Renderer r = list[i];

                if (Usable(r) && InFirstLod(r))
                    AddWanted(r, group);
            }
        }

        private static void AddWeaponSlot(PlayerBody body, EquipmentSlot slot, bool log)
        {
            if (body.SlotViews == null)
                return;

            PlayerBody.SlotView view;

            if (!body.SlotViews.TryGetByKey(slot, out view) || view == null)
            {
                if (log)
                    Plugin.Log.LogInfo("[BodyCamera] Body view: " + slot + " - no slot view.");

                return;
            }

            object item = view.ContainedItem;
            object model = view.Model;
            GameObject go = model as GameObject ?? (model is Component ? ((Component)model).gameObject : null);
            bool inHands = item != null && ReferenceEquals(item, _handsItem);

            _tmpRenderers.Clear();

            if (view.Renderers != null)
            {
                foreach (Renderer r in view.Renderers)
                {
                    if (r != null && !_tmpRenderers.Contains(r))
                        _tmpRenderers.Add(r);
                }
            }

            if (_tmpRenderers.Count == 0 && go != null)
                go.GetComponentsInChildren(true, _tmpRenderers);

            int enabled = 0, taken = 0;

            if (!inHands && go != null && go.activeInHierarchy)
            {
                for (int i = 0; i < _tmpRenderers.Count; i++)
                {
                    Renderer r = _tmpRenderers[i];

                    if (r == null || !(r is SkinnedMeshRenderer || r is MeshRenderer))
                        continue;

                    if (r.enabled)
                        enabled++;

                    if (r.gameObject.activeInHierarchy && InFirstLod(r))
                    {
                        AddWanted(r, Group.Weapons);
                        taken++;
                    }
                }
            }

            if (log)
                Plugin.Log.LogInfo(
                    "[BodyCamera] Body view: " + slot + " item " + (item != null ? item.ToString() : "none") +
                    ", model " + (go != null ? go.name + (go.activeInHierarchy ? "" : " (inactive)") : "none") +
                    ", in hands " + inHands + ", renderers " + _tmpRenderers.Count +
                    " (enabled " + enabled + "), taken " + taken + ".");
        }

        private static object HandsItem(Player player)
        {
            try
            {
                var hands = player.HandsController;
                return hands != null ? (object)hands.Item : null;
            }
            catch
            {
                return null;
            }
        }

        private static void AddWanted(Renderer r, Group group)
        {
            for (int i = 0; i < _wanted.Count; i++)
            {
                if (_wanted[i].Key == r)
                    return;
            }

            _wanted.Add(new KeyValuePair<Renderer, Group>(r, group));
        }

        private static bool InFirstLod(Renderer r)
        {
            LODGroup g = r.GetComponentInParent<LODGroup>();

            if (g == null)
                return true;

            HashSet<Renderer> set;
            LOD[] lods = g.GetLODs();

            if (!_lod0.TryGetValue(g, out set))
            {
                set = new HashSet<Renderer>();

                if (lods != null && lods.Length > 0 && lods[0].renderers != null)
                {
                    foreach (Renderer x in lods[0].renderers)
                    {
                        if (x != null)
                            set.Add(x);
                    }
                }

                _lod0[g] = set;
            }

            if (set.Contains(r))
                return true;

            for (int i = 1; lods != null && i < lods.Length; i++)
            {
                if (lods[i].renderers == null)
                    continue;

                foreach (Renderer x in lods[i].renderers)
                {
                    if (x == r)
                        return false;
                }
            }

            return true;
        }

        private static void Rebuild(PlayerBody body)
        {
            for (int i = 0; i < _copies.Count; i++)
            {
                if (_copies[i].Clone != null)
                    UnityEngine.Object.Destroy(_copies[i].Clone.gameObject);
            }

            _copies.Clear();

            if (_root == null)
            {
                _root = new GameObject("BodyCamera_BodyView");
                _root.transform.SetParent(body.transform, false);
                _root.transform.localPosition = Vector3.zero;
                _root.transform.localRotation = Quaternion.identity;
                _root.transform.localScale = Vector3.one;
            }

            CollectHands(body);

            int layer = _handsRenderers.Count > 0 && _handsRenderers[0] != null
                ? _handsRenderers[0].gameObject.layer
                : body.gameObject.layer;

            int[] counts = new int[GroupCount];
            bool hideNeck = !ShowHead();

            for (int i = 0; i < _wanted.Count; i++)
            {
                Renderer src = _wanted[i].Key;
                Group group = _wanted[i].Value;
                var smr = src as SkinnedMeshRenderer;
                Renderer clone;

                if (smr != null)
                {
                    Transform[] bones = smr.bones;

                    if (hideNeck && (group == Group.Torso || group == Group.Armor))
                        bones = CollapseNeck(bones);

                    clone = CloneSkinned(smr, layer, bones);
                }
                else
                {
                    clone = CloneRigid((MeshRenderer)src, layer);
                }

                if (clone == null)
                    continue;

                _copies.Add(new Copy { Source = src, Clone = clone, Group = group, Skinned = smr != null });
                counts[(int)group]++;
            }

            _hasTorsoCopy = counts[(int)Group.Torso] > 0;
            RequestFullTextures();
            RefreshBlocks();

            Plugin.Log.LogInfo(
                "[BodyCamera] Body view (" + (_third ? "third" : "first") + " person): torso " + counts[0] +
                ", armor " + counts[1] + ", head " + counts[2] +
                ", helmet " + counts[3] + ", backpack " + counts[4] +
                ", weapons " + counts[5] + " renderers.");
        }

        private static Renderer CloneSkinned(SkinnedMeshRenderer src, int layer, Transform[] bones)
        {
            if (src.sharedMesh == null)
                return null;

            var go = new GameObject("BodyCam_" + src.name);
            go.layer = layer;
            go.transform.SetParent(_root.transform, false);

            go.transform.position = src.transform.position;
            go.transform.rotation = src.transform.rotation;

            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = src.sharedMesh;
            smr.sharedMaterials = src.sharedMaterials;
            smr.bones = bones ?? src.bones;
            smr.rootBone = src.rootBone;
            smr.quality = src.quality;
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = ShadowCastingMode.Off;
            smr.receiveShadows = src.receiveShadows;
            smr.lightProbeUsage = src.lightProbeUsage;
            smr.reflectionProbeUsage = src.reflectionProbeUsage;

            Mesh mesh = src.sharedMesh;

            for (int i = 0; i < mesh.blendShapeCount; i++)
                smr.SetBlendShapeWeight(i, src.GetBlendShapeWeight(i));

            return smr;
        }

        private static Renderer CloneRigid(MeshRenderer src, int layer)
        {
            MeshFilter mf = src.GetComponent<MeshFilter>();

            if (mf == null || mf.sharedMesh == null)
                return null;

            var go = new GameObject("BodyCam_" + src.name);
            go.layer = layer;
            go.transform.SetParent(src.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var f = go.AddComponent<MeshFilter>();
            f.sharedMesh = mf.sharedMesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = src.sharedMaterials;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = src.receiveShadows;
            mr.lightProbeUsage = src.lightProbeUsage;
            mr.reflectionProbeUsage = src.reflectionProbeUsage;

            return mr;
        }

        private static void RefreshBlocks()
        {
            if (_mpb == null)
                _mpb = new MaterialPropertyBlock();

            for (int i = 0; i < _copies.Count; i++)
            {
                Copy c = _copies[i];

                if (c.Source == null || c.Clone == null)
                    continue;

                _mpb.Clear();
                c.Source.GetPropertyBlock(_mpb);
                c.Clone.SetPropertyBlock(_mpb);

                if (c.Clone.sharedMaterials.Length != c.Source.sharedMaterials.Length)
                    c.Clone.sharedMaterials = c.Source.sharedMaterials;

                int mats = c.Source.sharedMaterials.Length;

                for (int m = 0; m < mats; m++)
                {
                    _mpb.Clear();
                    c.Source.GetPropertyBlock(_mpb, m);

                    if (!_mpb.isEmpty)
                        c.Clone.SetPropertyBlock(_mpb, m);
                }
            }
        }

        private static Transform Point(Transform bone, string name)
        {
            Transform p;

            if (_points.TryGetValue(bone, out p) && p != null)
                return p;

            var go = new GameObject(name);
            p = go.transform;
            p.SetParent(bone.parent, false);
            p.localPosition = bone.localPosition;
            p.localRotation = Quaternion.identity;
            p.localScale = Vector3.one * CollapseScale;

            _points[bone] = p;
            return p;
        }

        private static string Lower(Transform t)
        {
            return t != null ? t.name.ToLowerInvariant() : "";
        }

        private static Transform[] CollapseNeck(Transform[] bones)
        {
            if (bones == null)
                return null;

            var result = (Transform[])bones.Clone();

            for (int i = 0; i < result.Length; i++)
            {
                Transform neck = FindUp(result[i], "neck", true);

                if (neck != null && neck.parent != null)
                    result[i] = Point(neck, "BodyCam_NeckPoint");
            }

            return result;
        }

        private static Transform FindUp(Transform b, string key, bool topMost)
        {
            Transform found = null;
            Transform t = b;

            for (int depth = 0; t != null && depth < 16; depth++, t = t.parent)
            {
                string n = Lower(t);

                if (n.Contains(key))
                {
                    found = t;

                    if (!topMost)
                        return found;
                }
                else if (n.Contains("forearm") || n.Contains("upperarm") ||
                         n.Contains("ribcage") || n.Contains("spine") ||
                         n.Contains("collarbone") || n.Contains("clavicle") ||
                         n.Contains("pelvis") || n.Contains("root"))
                {
                    break;
                }
            }

            return found;
        }

        private static void CollectHands(PlayerBody body)
        {
            _handsRenderers.Clear();

            try
            {
                LoddedSkin hands;

                if (body.BodySkins != null &&
                    body.BodySkins.TryGetValue(EBodyModelPart.Hands, out hands) &&
                    hands != null)
                {
                    foreach (Renderer r in hands.GetRenderers())
                    {
                        if (r != null && !_handsRenderers.Contains(r))
                            _handsRenderers.Add(r);
                    }
                }
            }
            catch
            {
            }
        }

        private static void UpdateHands()
        {
            if (!_hasTorsoCopy)
            {
                RestoreHands();
                return;
            }

            for (int i = 0; i < _handsRenderers.Count; i++)
            {
                Renderer r = _handsRenderers[i];

                if (r == null || !r.enabled)
                    continue;

                r.enabled = false;

                if (!_hiddenHands.Contains(r))
                    _hiddenHands.Add(r);
            }
        }

        private static void RestoreHands()
        {
            if (_hiddenHands.Count == 0)
                return;

            bool firstPerson = _player != null && _player.PointOfView == EPointOfView.FirstPerson;

            for (int i = 0; i < _hiddenHands.Count; i++)
            {
                Renderer r = _hiddenHands[i];

                if (r != null && firstPerson)
                    r.enabled = true;
            }

            _hiddenHands.Clear();
        }

        private static void RequestFullTextures()
        {
            ReleaseTextures();

            for (int i = 0; i < _copies.Count; i++)
            {
                Copy c = _copies[i];

                if ((c.Group != Group.Torso && c.Group != Group.Head) || c.Clone == null)
                    continue;

                Material[] mats = c.Clone.sharedMaterials;

                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];

                    if (mat == null)
                        continue;

                    int[] ids = mat.GetTexturePropertyNameIDs();

                    for (int t = 0; t < ids.Length; t++)
                    {
                        var tex = mat.GetTexture(ids[t]) as Texture2D;

                        if (tex == null || !tex.streamingMipmaps || _fullTextures.Contains(tex))
                            continue;

                        if (!_loggedTextures)
                        {
                            _loggedTextures = true;
                            Plugin.Log.LogInfo(
                                "[BodyCamera] Body view: torso texture " + tex.name + " " + tex.width + "x" + tex.height +
                                ", loaded mip " + tex.loadedMipmapLevel + " - asking for mip 0.");
                        }

                        tex.requestedMipmapLevel = 0;
                        _fullTextures.Add(tex);
                    }
                }
            }
        }

        private static void ReleaseTextures()
        {
            for (int i = 0; i < _fullTextures.Count; i++)
            {
                try
                {
                    if (_fullTextures[i] != null)
                        _fullTextures[i].ClearRequestedMipmapLevel();
                }
                catch
                {
                }
            }

            _fullTextures.Clear();
        }
    }

    internal static class BodyCamThirdEffects
    {
        private static Camera _cam;
        private static Behaviour[] _parts;
        private static bool[] _want;
        private static Behaviour[] _vision;
        private static bool[] _real;
        private static bool _off;
        private static bool _keepVision = true;
        private static bool _forcing;
        private static FieldInfo _holders;
        private static bool _holdersLooked;

        internal static void Tick(Camera cam, bool third)
        {
            if (third && cam != null)
            {
                bool keep = Plugin.ThirdPersonVisionEffects == null || Plugin.ThirdPersonVisionEffects.Value;

                if (!_off || cam != _cam || keep != _keepVision)
                {
                    Restore();
                    _keepVision = keep;
                    Collect(cam);
                    _off = true;
                }

                if (_vision != null)
                {
                    for (int i = 0; i < _vision.Length; i++)
                    {
                        Behaviour v = _vision[i];

                        if (v != null && IsOn(v))
                            SetVision(v, false);
                    }
                }

                for (int i = 0; i < _parts.Length; i++)
                {
                    Behaviour b = _parts[i];

                    if (b == null || !b.enabled)
                        continue;

                    _want[i] = true;
                    b.enabled = false;
                }
            }
            else if (_off)
            {
                Restore();
            }
        }

        internal static void NoteSwitch(Behaviour b, bool on)
        {
            if (_forcing || _vision == null)
                return;

            for (int i = 0; i < _vision.Length; i++)
            {
                if (ReferenceEquals(_vision[i], b))
                    _real[i] = on;
            }
        }

        private static void Collect(Camera cam)
        {
            _cam = cam;
            var list = new List<Behaviour>();
            var vision = new List<Behaviour>();

            try
            {
                list.AddRange(cam.GetComponents<global::VisorEffect>());
                list.AddRange(cam.GetComponents<BSG.CameraEffects.TextureMask>());

                if (!_keepVision)
                {
                    vision.AddRange(cam.GetComponents<BSG.CameraEffects.NightVision>());
                    vision.AddRange(cam.GetComponents<global::ThermalVision>());
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[BodyCamera] Third person effects: " + ex.Message);
            }

            _parts = list.ToArray();
            _want = new bool[_parts.Length];

            for (int i = 0; i < _parts.Length; i++)
                _want[i] = _parts[i] != null && _parts[i].enabled;

            if (vision.Count > 0)
            {
                _vision = vision.ToArray();
                _real = new bool[_vision.Length];

                for (int i = 0; i < _vision.Length; i++)
                {
                    _real[i] = _vision[i] != null && IsOn(_vision[i]);

                    if (_real[i])
                        SetVision(_vision[i], false);
                }
            }
            else
            {
                _vision = null;
                _real = null;
            }
        }

        private static void Restore()
        {
            if (_off && _vision != null)
            {
                for (int i = 0; i < _vision.Length; i++)
                {
                    Behaviour v = _vision[i];

                    if (v != null && IsOn(v) != _real[i])
                        SetVision(v, _real[i]);
                }
            }

            if (_off && _parts != null)
            {
                bool vision = false;

                try
                {
                    if (_cam != null)
                    {
                        foreach (var n in _cam.GetComponents<BSG.CameraEffects.NightVision>())
                            vision |= n != null && n.On;

                        foreach (var t in _cam.GetComponents<global::ThermalVision>())
                            vision |= t != null && t.On;
                    }
                }
                catch
                {
                }

                for (int i = 0; i < _parts.Length; i++)
                {
                    Behaviour b = _parts[i];

                    if (b == null)
                        continue;

                    bool on;

                    if (b is BSG.CameraEffects.TextureMask)
                    {
                        int held = Holders(b);
                        on = held >= 0 ? held > 0 : vision;
                    }
                    else
                    {
                        on = _want[i];
                    }

                    try
                    {
                        b.enabled = on;
                    }
                    catch
                    {
                    }
                }
            }

            _off = false;
            _parts = null;
            _want = null;
            _vision = null;
            _real = null;
            _cam = null;
        }

        private static bool IsOn(Behaviour b)
        {
            if (b is BSG.CameraEffects.NightVision nv)
                return nv.On;

            if (b is global::ThermalVision tv)
                return tv.On;

            return false;
        }

        private static void SetVision(Behaviour b, bool on)
        {
            _forcing = true;

            try
            {
                if (b is BSG.CameraEffects.NightVision nv)
                    nv.Switch(on);
                else if (b is global::ThermalVision tv)
                    tv.Switch(on);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[BodyCamera] Third person vision switch: " + ex.Message);
            }
            finally
            {
                _forcing = false;
            }
        }

        private static int Holders(Behaviour mask)
        {
            try
            {
                if (!_holdersLooked)
                {
                    _holdersLooked = true;
                    _holders = typeof(BSG.CameraEffects.TextureMask).GetField(
                        "_textureMaskHolders", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }

                object set = _holders != null ? _holders.GetValue(mask) : null;

                if (set == null)
                    return -1;

                PropertyInfo count = set.GetType().GetProperty("Count");
                return count != null ? (int)count.GetValue(set, null) : -1;
            }
            catch
            {
                return -1;
            }
        }
    }

    internal sealed class BodyCamNightVisionSwitchPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(BSG.CameraEffects.NightVision).GetMethod(
                "Switch", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(bool) }, null);
        }

        [PatchPostfix]
        private static void Postfix(BSG.CameraEffects.NightVision __instance, bool __0)
        {
            BodyCamThirdEffects.NoteSwitch(__instance, __0);
        }
    }

    internal sealed class BodyCamThermalSwitchPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(global::ThermalVision).GetMethod(
                "Switch", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(bool) }, null);
        }

        [PatchPostfix]
        private static void Postfix(global::ThermalVision __instance, bool __0)
        {
            BodyCamThirdEffects.NoteSwitch(__instance, __0);
        }
    }
}
