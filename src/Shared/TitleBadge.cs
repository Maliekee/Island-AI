// Copyright (c) 2026 Maliekee
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Shared
{
    // Each loaded Island mod puts its icon on the title screen: "it is installed, and it
    // loaded" - the question a player has after unzipping a mod, answered before a save loads.
    // Just the icon, in one row (the first version was a wooden plate with name
    // and version per mod, stacked - too much for a corner of someone else's menu). Name,
    // version and state come up in a small tip while the pointer is on the icon, and a click
    // switches the mod on or off - here and nowhere else, so never in the middle of a game.
    //
    // A mod tells the badge two things, as functions because both can change under it:
    //   running  is the mod doing anything right now?
    //   wanted   what the saved setting says
    // A mod whose switch is live (Island AI) answers both from the one setting and they never
    // differ. One that reads its switch once at start (Island UI: its teardown is only safe when
    // a fresh assembly follows, so it cannot come back on in the same process) keeps `running`
    // fixed, and the two differ from the click until the restart. That difference is what the
    // badge shows: the icon's colour is what is RUNNING, the coral pip means "changes at the next
    // start", and the tip says it in words. A disabled mod still shows its icon, grey, or there
    // would be no way back short of editing a .cfg.
    //
    // Shared SOURCE, like PatchCensus: each mod compiles its own copy and knows nothing of the
    // others, so what they agree on cannot be a type. It is a GameObject NAME. The first mod to
    // reach the title scene builds the row (an overlay canvas, bottom-left beside vanilla's
    // build stamp); every other one finds it by that name and adds its icon. Remove either DLL
    // and the other's icon is simply alone.
    //
    // The icon is an embedded PNG (tools/ui-kit/make_mod_icon.py), so it needs no kit art at
    // runtime, which keeps Island AI a single file; the grey icon and the pip are made here, and
    // the tip is two flat rectangles and text in a font borrowed from the title screen.
    //
    // The row is an ordinary object of the title scene: it goes when the scene does, and is
    // rebuilt when the player returns to the title.
    internal static class TitleBadge
    {
        private const string RowName = "IslandModBadges";
        private const float Size = 52f;                      // on a 1920x1080 reference canvas

        private static readonly Color32 Ink = new Color32(0x28, 0x1A, 0x10, 0xFF);
        private static readonly Color32 DarkPaper = new Color32(0x3A, 0x2A, 0x1C, 0xFF);
        private static readonly Color32 Coral = new Color32(0xEE, 0x54, 0x40, 0xFF);

        private static Assembly _assembly;
        private static string _resource;
        private static string _caption;
        private static Func<bool> _running;
        private static Func<bool> _wanted;
        private static Action<bool> _setWanted;

        private static Sprite _sprite;
        private static Sprite _grey;
        private static Sprite _pipSprite;
        private static GameObject _badge;
        private static Image _image;
        private static GameObject _pip;
        private static Text _text;

        internal static void Install(Assembly assembly, string resource, string title, string version,
            Func<bool> running, Func<bool> wanted, Action<bool> setWanted)
        {
            _assembly = assembly;
            _resource = resource;
            _caption = title + "  v" + version;
            _running = running;
            _wanted = wanted;
            _setWanted = setWanted;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Show(SceneManager.GetActiveScene());
        }

        // Hot reload only: the next generation installs its own.
        internal static void Remove()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_badge != null)
            {
                UnityEngine.Object.Destroy(_badge);
            }
            _badge = null;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Show(scene);
        }

        private static void Show(Scene scene)
        {
            if (_badge != null || !scene.IsValid() || !scene.name.StartsWith("title", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            try
            {
                if (!LoadSprites())
                {
                    return;
                }
                _badge = Build(Row());
                Refresh();
            }
            catch (Exception e)
            {
                // a badge that fails must never cost the player the mod
                Debug.LogWarning("TitleBadge: " + e.Message);
            }
        }

        private static void Refresh()
        {
            if (_badge == null)
            {
                return;
            }
            bool running = _running(), wanted = _wanted();
            _image.sprite = running ? _sprite : _grey;
            // still running but on its way out: visibly answered, without claiming it is off yet
            _image.color = running && !wanted ? new Color(0.55f, 0.55f, 0.55f, 1f)
                : running ? Color.white : new Color(0.62f, 0.62f, 0.62f, 1f);
            _pip.SetActive(running != wanted);
            string state = running
                ? (wanted ? "on  -  click to turn off" : "turns off at the next start  -  click to cancel")
                : (wanted ? "turns on at the next start  -  click to cancel" : "off  -  click to turn on");
            _text.text = _caption + "\n<color=#CEA064>" + state + "</color>";
        }

        private static Transform Row()
        {
            GameObject root = GameObject.Find(RowName);
            if (root != null)
            {
                return root.transform.GetChild(0);
            }
            // the raycaster is for the tips and the click; only the icons themselves are raycast
            // targets, so every click anywhere else still reaches the game's own menu
            root = new GameObject(RowName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            var rect = (RectTransform)row.transform;
            rect.SetParent(root.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;       // bottom-left, growing rightward
            // right of vanilla's own "Build_2026/09/03--11:45" stamp, which owns the corner itself:
            // it ends about 283 units in on this 1080p canvas
            rect.anchoredPosition = new Vector2(304f, 12f);     // a little air under the row
            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = row.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        private static GameObject Build(Transform row)
        {
            var badge = new GameObject("Badge_" + _resource, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            badge.transform.SetParent(row, false);
            _image = badge.GetComponent<Image>();
            _image.sprite = _sprite;
            LayoutElement size = badge.GetComponent<LayoutElement>();
            size.preferredWidth = size.preferredHeight = Size;

            // "changes at the next start": on the icon's top-right corner, there after the pointer leaves
            _pip = new GameObject("Pip", typeof(RectTransform), typeof(Image));
            var pipRect = (RectTransform)_pip.transform;
            pipRect.SetParent(badge.transform, false);
            pipRect.anchorMin = pipRect.anchorMax = Vector2.one;
            pipRect.pivot = new Vector2(0.5f, 0.5f);
            pipRect.anchoredPosition = new Vector2(-5f, -5f);
            pipRect.sizeDelta = new Vector2(18f, 18f);
            Image pipImage = _pip.GetComponent<Image>();
            pipImage.sprite = _pipSprite;
            pipImage.raycastTarget = false;

            GameObject tip = BuildTip(badge.transform);
            EventTrigger trigger = badge.AddComponent<EventTrigger>();
            Listen(trigger, EventTriggerType.PointerEnter, () => tip.SetActive(true));
            Listen(trigger, EventTriggerType.PointerExit, () => tip.SetActive(false));
            Listen(trigger, EventTriggerType.PointerClick, () =>
            {
                _setWanted(!_wanted());
                Refresh();
            });
            return badge;
        }

        private static void Listen(EventTrigger trigger, EventTriggerType type, Action act)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ =>
            {
                try { act(); }
                catch (Exception e) { Debug.LogWarning("TitleBadge: " + e.Message); }
            });
            trigger.triggers.Add(entry);
        }

        // Above the icon, left edges together: an ink rim, dark paper, the caption in cream and
        // the state under it in pine - the kit's dark tooltip drawn with flat colours,
        // square-cornered, sized by its text.
        private static GameObject BuildTip(Transform badge)
        {
            var tip = new GameObject("Tip", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            var rect = (RectTransform)tip.transform;
            rect.SetParent(badge, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(0f, 6f);
            Frame(tip, Ink, 2, 2);

            var paper = new GameObject("Paper", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            paper.transform.SetParent(tip.transform, false);
            Frame(paper, DarkPaper, 10, 6);

            var label = new GameObject("Caption", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(paper.transform, false);
            _text = label.GetComponent<Text>();
            Font font = null;
            foreach (Text theirs in UnityEngine.Object.FindObjectsOfType<Text>())
            {
                if (theirs != _text && theirs.font != null)
                {
                    font = theirs.font;
                    break;
                }
            }
            _text.font = font != null ? font : Resources.GetBuiltinResource<Font>("Arial.ttf");
            _text.fontSize = 18;
            _text.lineSpacing = 1.1f;
            _text.supportRichText = true;
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.color = new Color32(0xEA, 0xD4, 0xB0, 0xFF);                  // the kit's cream
            _text.raycastTarget = false;

            ContentSizeFitter fitter = tip.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            tip.SetActive(false);
            return tip;
        }

        private static void Frame(GameObject go, Color32 colour, int padX, int padY)
        {
            Image image = go.GetComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            HorizontalLayoutGroup layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(padX, padX, padY, padY);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        }

        private static bool LoadSprites()
        {
            if (_sprite != null)
            {
                return true;
            }
            byte[] bytes;
            using (Stream stream = _assembly.GetManifestResourceStream(_resource))
            {
                if (stream == null)
                {
                    Debug.LogWarning("TitleBadge: no embedded resource '" + _resource + "'");
                    return false;
                }
                bytes = new byte[stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = stream.Read(bytes, read, bytes.Length - read);
                    if (n <= 0)
                    {
                        break;
                    }
                    read += n;
                }
            }
            // mip-mapped: a 192 px icon shown at a fraction of that shimmers without them
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            texture.LoadImage(bytes);
            _sprite = AsSprite(texture);

            // the same icon with the colour taken out, for a mod that is not running
            Color32[] pixels = texture.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                byte luma = (byte)((p.r * 77 + p.g * 150 + p.b * 29) >> 8);
                pixels[i] = new Color32(luma, luma, luma, p.a);
            }
            var grey = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, true);
            grey.SetPixels32(pixels);
            grey.Apply(true);
            _grey = AsSprite(grey);

            _pipSprite = AsSprite(RoundedSquare(48, 13f, 6f, Coral, Ink));
            return true;
        }

        private static Sprite AsSprite(Texture2D texture)
        {
            texture.filterMode = FilterMode.Trilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        }

        // The icon's own shape, small: a rounded square with a rim, from its distance field.
        private static Texture2D RoundedSquare(int size, float radius, float rim, Color32 fill, Color32 edge)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var pixels = new Color32[size * size];
            float half = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                    float d = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;     // < 0 inside
                    Color32 c = d > -rim ? edge : fill;
                    c.a = (byte)(Mathf.Clamp01(0.5f - d) * 255f);
                    pixels[y * size + x] = c;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }
    }
}
