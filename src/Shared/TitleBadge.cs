using System;
using System.Collections.Generic;
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
    // opens the mod's menu: its on/off switch - here and nowhere else, so never in the middle of
    // a game - whatever else the mod adds through Extras (Island UI and Island Ex: the update,
    // Shared/Update/ModUpdate.cs), and the way to the mod's official site.
    //
    // A mod tells the badge two things, as functions because both can change under it:
    //   running  is the mod doing anything right now?
    //   wanted   what the saved setting says
    // A mod whose switch is live (Island AI) answers both from the one setting and they never
    // differ. One that reads its switch once at start (Island UI: its teardown is only safe when
    // a fresh assembly follows, so it cannot come back on in the same process) keeps `running`
    // fixed, and the two differ from the click until the restart. That difference is what the
    // badge shows: the icon's colour is what is RUNNING, the coral pip means "changes at the next
    // start", and the tip says it in words. Note adds to that: anything else about to change
    // at the next start, which lights the same pip. A disabled mod still shows its icon, grey, or
    // there would be no way back short of editing a .cfg.
    //
    // Sign is the other mark, and it means nothing will change by itself: a newer release exists,
    // the player's to fetch or not. So it looks nothing like the pip: a green disc with an arrow
    // at the bottom right, where the pip is a coral square at the top right.
    //
    // Shared SOURCE, like PatchCensus: each mod compiles its own copy and knows nothing of the
    // others, so what they agree on cannot be a type. It is a GameObject NAME. The first mod to
    // reach the title scene builds the row (an overlay canvas, bottom-left beside vanilla's
    // build stamp); every other one finds it by that name and adds its icon. Remove either DLL
    // and the other's icon is simply alone.
    //
    // The icon is an embedded PNG (made by a dev script), so it needs no kit art at
    // runtime, which keeps Island AI a single file; the grey icon and both marks are made here, and
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
        private static readonly Color32 Hover = new Color32(0x55, 0x3E, 0x2A, 0xFF);
        private static readonly Color32 Leaf = new Color32(0x4E, 0xB0, 0x6A, 0xFF);
        private static readonly Color32 Cream = new Color32(0xEA, 0xD4, 0xB0, 0xFF);

        // written here and nowhere else: nothing from a release or a response goes into it. Island
        // AI is a repository of its own; Island Ex is released from Island UI's.
#if ISLAND_AI
        private const string OfficialSite = "https://github.com/Maliekee/Island-AI";
#else
        private const string OfficialSite = "https://github.com/Maliekee/Island-UI";
#endif

        private static Assembly _assembly;
        private static string _resource;
        private static string _caption;
        private static Func<bool> _running;
        private static Func<bool> _wanted;
        private static Action<bool> _setWanted;

        // one line of a badge's menu
        internal sealed class Item
        {
            internal readonly string Label;
            internal readonly Action Act;
            internal readonly bool Stay;        // a setting: the menu stays up to show its new state

            internal Item(string label, Action act, bool stay = false)
            {
                Label = label;
                Act = act;
                Stay = stay;
            }
        }

        // a mod with more to offer than its switch sets these; Island AI sets none
        internal static Func<List<Item>> Extras;
        internal static Func<string> Note;
        internal static Func<string> Sign;      // words for the update sign; empty, no sign
        internal static Action AtTitle;
        internal static Action Tick;            // every frame while the badge exists

        private const string MenuName = "IslandBadgeMenu";
        private const string BlockerName = "IslandBadgeMenuBlocker";
        private const string RefreshMessage = "RefreshBadge";

        private static Sprite _sprite;
        private static Sprite _grey;
        private static Sprite _pipSprite;
        private static GameObject _badge;
        private static Image _image;
        private static GameObject _pip;
        private static Sprite _signSprite;
        private static GameObject _sign;
        private static Text _text;
        private static GameObject _tip;
        private static GameObject _menu;
        private static Font _font;

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
            CloseMenu();
            if (_badge != null)
            {
                UnityEngine.Object.Destroy(_badge);
            }
            _badge = null;
        }

        // is this mod doing anything right now? (Island UI: what it found at this start)
        internal static bool Running
        {
            get { return _running(); }
        }

        // the row's canvas, for a window that belongs with the badges
        internal static Transform Canvas()
        {
            GameObject root = GameObject.Find(RowName);
            return root != null ? root.transform : null;
        }

        // the title screen's own font, once a badge has found it
        internal static Font TitleFont()
        {
            if (_font == null)
            {
                foreach (Text theirs in UnityEngine.Object.FindObjectsOfType<Text>())
                {
                    if (theirs.font != null)
                    {
                        _font = theirs.font;
                        break;
                    }
                }
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
            }
            return _font;
        }

        // for a mod that finishes loading after its badge went up
        internal static void TitleCheck()
        {
            if (_badge != null)
            {
                Safely(AtTitle);
            }
        }

        private static void Safely(Action act)
        {
            try { act?.Invoke(); }
            catch (Exception e) { Debug.LogWarning("TitleBadge: " + e.Message); }
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
                Safely(AtTitle);
            }
            catch (Exception e)
            {
                // a badge that fails must never cost the player the mod
                Debug.LogWarning("TitleBadge: " + e.Message);
            }
        }

        // every mod's badge, this copy's and the others': an update changes more than one, and
        // the copies share no type, so it goes by a Unity message by name
        internal static void RefreshAll()
        {
            Transform canvas = Canvas();
            if (canvas != null)
            {
                canvas.BroadcastMessage(RefreshMessage, SendMessageOptions.DontRequireReceiver);
            }
        }

        private sealed class Listener : MonoBehaviour
        {
            private void RefreshBadge()
            {
                Refresh();
            }

            private void Update()
            {
                Safely(Tick);
            }
        }

        internal static void Refresh()
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
            string note = Note != null ? Note() : "";
            string sign = Sign != null ? Sign() : "";
            _pip.SetActive(running != wanted || note.Length > 0);
            _sign.SetActive(sign.Length > 0);
            _text.text = Words(note, sign);
        }

        private static string Words(string note, string sign)
        {
            bool running = _running(), wanted = _wanted();
            string state = running
                ? (wanted ? Loc.Get("badge.state_on", "on") : Loc.Get("badge.state_turns_off", "turns off at the next start"))
                : (wanted ? Loc.Get("badge.state_turns_on", "turns on at the next start") : Loc.Get("badge.state_off", "off"));
            return _caption + "\n<color=#CEA064>" + state + (note.Length > 0 ? "\n" + note : "")
                + (sign.Length > 0 ? "\n" + sign : "") + "</color>";
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

            // "there is a newer release": the bottom corner, a round mark, its own colour
            _sign = new GameObject("Sign", typeof(RectTransform), typeof(Image));
            var signRect = (RectTransform)_sign.transform;
            signRect.SetParent(badge.transform, false);
            signRect.anchorMin = signRect.anchorMax = new Vector2(1f, 0f);
            signRect.pivot = new Vector2(0.5f, 0.5f);
            signRect.anchoredPosition = new Vector2(-4f, 4f);
            signRect.sizeDelta = new Vector2(20f, 20f);
            Image signImage = _sign.GetComponent<Image>();
            signImage.sprite = _signSprite;
            signImage.raycastTarget = false;
            _sign.SetActive(false);

            _tip = BuildTip(badge.transform);
            badge.AddComponent<Listener>();
            EventTrigger trigger = badge.AddComponent<EventTrigger>();
            // lettered as it opens, so the words are in the language of the moment
            Listen(trigger, EventTriggerType.PointerEnter, () =>
            {
                Refresh();
                _tip.SetActive(_menu == null);
            });
            Listen(trigger, EventTriggerType.PointerExit, () => _tip.SetActive(false));
            Listen(trigger, EventTriggerType.PointerClick, () =>
            {
                if (_menu != null)
                {
                    CloseMenu();
                }
                else
                {
                    OpenMenu(badge.transform);
                }
            });
            return badge;
        }

        // Above the icon where the tip stands, and in the tip's dress: who it is, how it stands,
        // and what can be done about it. The mod's switch comes first and says what a click does
        // from here. The first version of this badge had the click do that directly.
        private static void OpenMenu(Transform badge)
        {
            CloseMenu();
            CloseOthers();
            _tip.SetActive(false);

            // behind the row on the same canvas: a click anywhere else closes the menu and stops
            // there, and a click on another mod's icon still reaches that icon
            var blocker = new GameObject(BlockerName, typeof(RectTransform), typeof(Image));
            var blockRect = (RectTransform)blocker.transform;
            blockRect.SetParent(Canvas(), false);
            blockRect.SetAsFirstSibling();
            blockRect.anchorMin = Vector2.zero;
            blockRect.anchorMax = Vector2.one;
            blockRect.offsetMin = blockRect.offsetMax = Vector2.zero;
            blocker.GetComponent<Image>().color = Color.clear;
            Listen(blocker.AddComponent<EventTrigger>(), EventTriggerType.PointerClick, CloseMenu);

            _menu = new GameObject(MenuName, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var rect = (RectTransform)_menu.transform;
            rect.SetParent(badge, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(0f, 6f);
            MenuFrame(_menu, Ink, 2, 2, 0f);
            ContentSizeFitter fit = _menu.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var paper = new GameObject("Paper", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            paper.transform.SetParent(_menu.transform, false);
            MenuFrame(paper, DarkPaper, 4, 4, 2f);

            string note = Note != null ? Note() : "";
            string sign = Sign != null ? Sign() : "";
            Text heading = Label(paper.transform, Words(note, sign));
            heading.supportRichText = true;

            MenuRow(paper.transform, SwitchWords(), () =>
            {
                _setWanted(!_wanted());
                Refresh();
                CloseMenu();
            });
            if (Extras != null)
            {
                foreach (Item item in Extras())
                {
                    Action act = item.Act;
                    bool stay = item.Stay;
                    MenuRow(paper.transform, item.Label, () =>
                    {
                        if (stay)
                        {
                            act();
                            OpenMenu(badge);
                            return;
                        }
                        CloseMenu();
                        act();
                    });
                }
            }
            MenuRow(paper.transform, Loc.Get("badge.visit_site", "Visit the official site"), () =>
            {
                CloseMenu();
                Application.OpenURL(OfficialSite);
            });
        }

        private static string SwitchWords()
        {
            bool running = _running(), wanted = _wanted();
            return running
                ? (wanted ? Loc.Get("badge.turn_off", "Turn off") : Loc.Get("badge.keep_on", "Keep it on"))
                : (wanted ? Loc.Get("badge.keep_off", "Keep it off") : Loc.Get("badge.turn_on", "Turn on"));
        }

        private static void MenuRow(Transform parent, string words, Action act)
        {
            var row = new GameObject("Item", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent, false);
            Image image = row.GetComponent<Image>();
            image.color = Color.clear;
            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 16, 4, 4);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            Label(row.transform, words);
            EventTrigger trigger = row.AddComponent<EventTrigger>();
            Listen(trigger, EventTriggerType.PointerEnter, () => image.color = Hover);
            Listen(trigger, EventTriggerType.PointerExit, () => image.color = Color.clear);
            Listen(trigger, EventTriggerType.PointerClick, act);
        }

        private static Text Label(Transform parent, string words)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text text = go.GetComponent<Text>();
            text.font = TitleFont();
            text.fontSize = 18;
            text.lineSpacing = 1.1f;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = new Color32(0xEA, 0xD4, 0xB0, 0xFF);
            text.raycastTarget = false;
            text.text = words;
            return text;
        }

        private static void MenuFrame(GameObject go, Color32 colour, int padX, int padY, float spacing)
        {
            Image image = go.GetComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            VerticalLayoutGroup layout = go.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(padX, padX, padY, padY);
            layout.spacing = spacing;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;       // a row's hover spans the menu
            layout.childForceExpandHeight = false;
        }

        private static void CloseMenu()
        {
            if (_menu != null)
            {
                UnityEngine.Object.Destroy(_menu);
                _menu = null;
            }
            CloseOthers();
        }

        // another mod's copy of this class opened its own menu: the names are all that is shared
        private static void CloseOthers()
        {
            Transform canvas = Canvas();
            Transform blocker = canvas != null ? canvas.Find(BlockerName) : null;
            if (blocker != null)
            {
                UnityEngine.Object.Destroy(blocker.gameObject);
            }
            GameObject other = GameObject.Find(MenuName);
            if (other != null && other != _menu)
            {
                UnityEngine.Object.Destroy(other);
            }
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
            _text.font = TitleFont();
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
            _signSprite = AsSprite(UpArrowDisc(56, 5f, Leaf, Ink, Cream));
            return true;
        }

        private static Sprite AsSprite(Texture2D texture)
        {
            texture.filterMode = FilterMode.Trilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        }

        // A disc with a rim and an arrow pointing up, from their distance fields.
        private static Texture2D UpArrowDisc(int size, float rim, Color32 fill, Color32 edge, Color32 arrow)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var pixels = new Color32[size * size];
            float half = size / 2f;
            var tip = new Vector2(half, size * 0.76f);
            var foot = new Vector2(half, size * 0.24f);
            var left = new Vector2(size * 0.27f, size * 0.50f);
            var right = new Vector2(size * 0.73f, size * 0.50f);
            float stroke = size * 0.085f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var at = new Vector2(x + 0.5f, y + 0.5f);
                    float d = (at - new Vector2(half, half)).magnitude - half + 0.5f;      // < 0 inside
                    float a = Mathf.Min(Segment(at, tip, foot), Mathf.Min(Segment(at, tip, left), Segment(at, tip, right))) - stroke;
                    Color32 c = d > -rim ? edge : a < 0f ? arrow : fill;
                    c.a = (byte)(Mathf.Clamp01(0.5f - d) * 255f);
                    pixels[y * size + x] = c;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }

        private static float Segment(Vector2 at, Vector2 a, Vector2 b)
        {
            Vector2 along = b - a;
            return (at - (a + along * Mathf.Clamp01(Vector2.Dot(at - a, along) / along.sqrMagnitude))).magnitude;
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
