using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Shared
{
    // The update's window, on the title screen, over everything: the steps a player takes,
    // one screen each. It starts nothing itself; every step past the one on screen waits for a
    // button. Flat colours and a borrowed font like the badge's tip, so Island Ex needs no art.
    //
    // Text from the release is shown with rich text off, wherever it comes from.
    internal sealed class UpdatePanel : MonoBehaviour
    {
        private const string PanelName = "IslandUpdatePanel";
        private const float Width = 760f;

        private static readonly Color32 Ink = new Color32(0x28, 0x1A, 0x10, 0xFF);
        private static readonly Color32 Paper = new Color32(0x3A, 0x2A, 0x1C, 0xFF);
        private static readonly Color32 Lift = new Color32(0x55, 0x3E, 0x2A, 0xFF);
        private static readonly Color32 Cream = new Color32(0xEA, 0xD4, 0xB0, 0xFF);
        private static readonly Color32 Tan = new Color32(0xCE, 0xA0, 0x64, 0xFF);
        private static readonly Color32 Coral = new Color32(0xEE, 0x54, 0x40, 0xFF);

        private UpdateFlow _flow;
        private UpdateDisk _disk;
        private UpdateFlow.Step _shown = UpdateFlow.Step.Closed;
        private Transform _body;
        private Transform _footer;
        private Text _title;
        private Text _progress;
        private RectTransform _bar;

        // --- the ways in
        internal static void OpenCheck(UpdateFlow flow, UpdateDisk disk)
        {
            UpdatePanel panel = Create();
            if (panel != null)
            {
                panel._flow = flow;
                panel._disk = disk;
                flow.Check();
            }
        }

        internal static void ConfirmBack(UpdateDisk disk, string back)
        {
            UpdatePanel panel = Create();
            if (panel == null)
            {
                return;
            }
            panel.Say(Loc.Get("update.back_title", "Go back to the previous version?"),
                Loc.Get("update.back_body", "{0} comes back the next time you start the game, in place of what is installed now.", back),
                false);
            panel.Button(Loc.Get("update.back_do", "Go back"), true, () =>
            {
                disk.MarkUndo();
                TitleBadge.RefreshAll();
                panel.Close();
            });
            panel.Button(Loc.Get("update.back_cancel", "Cancel"), false, panel.Close);
        }

        internal static void ShowOptionNotSaved()
        {
            UpdatePanel panel = Create();
            if (panel == null)
            {
                return;
            }
            panel.Say(Loc.Get("update.option_title", "That was not saved"),
                Loc.Get("update.option_failed", "Windows refused to write to the game folder, so Check at startup stays as it was."), true);
            panel.Button(Loc.Get("update.close", "Close"), true, panel.Close);
        }

        // what the last start has to report, once
        internal static void ShowNotice(string notice)
        {
            UpdatePanel panel = Create();
            if (panel == null)
            {
                return;
            }
            string[] parts = notice.Split(new[] { '\n' }, 2);
            string detail = parts.Length > 1 ? parts[1] : "";
            bool bad = parts[0] != "went-back";
            panel.Say(bad ? Loc.Get("update.notice_title", "The update did not go through") : Loc.Get("update.notice_back_title", "Back on the previous version"),
                NoticeText(parts[0], detail), bad);
            panel.Button(Loc.Get("update.close", "Close"), true, panel.Close);
        }

        private static string NoticeText(string kind, string detail)
        {
            switch (kind)
            {
                case "went-back": return Loc.Get("update.notice_went_back", "Undone: {0}.", detail);
                case "not-loaded": return Loc.Get("update.notice_not_loaded", "The new version did not finish loading, so the previous version was put back. Undone: {0}.", detail);
                case "interrupted": return Loc.Get("update.notice_interrupted", "The last start stopped in the middle of installing, so the previous version was put back.");
                case "swap-failed": return Loc.Get("update.notice_swap_failed", "A file could not be replaced, so the previous version was put back.");
                case "changed": return Loc.Get("update.notice_changed", "The mod's files changed since the update was prepared, so it was dropped and nothing was replaced.");
                case "staged-damaged": return Loc.Get("update.notice_damaged", "The prepared files were damaged, so nothing was replaced. Check for updates to try again.");
                case "no-previous": return Loc.Get("update.notice_no_previous", "The previous version is no longer there, so nothing was changed.");
                default: return Loc.Get("update.notice_failed", "The update could not be installed: {0}. If the mod misbehaves, install it again from the release page.", kind + " " + detail);
            }
        }

        private static string FailureText(string kind, string detail)
        {
            switch (kind)
            {
                case "offline": return Loc.Get("update.fail_offline", "GitHub could not be reached. Check your connection and try again.");
                case "timeout": return Loc.Get("update.fail_timeout", "GitHub stopped answering. Try again in a while.");
                case "rate-limited": return Loc.Get("update.fail_rate", "GitHub is limiting requests from your connection right now. Try again in an hour or so.");
                case "secure": return Loc.Get("update.fail_secure", "The connection to GitHub could not be verified as secure, so it was refused.");
                case "host": return Loc.Get("update.fail_host", "The release pointed at an address that is not GitHub's ({0}), so it was refused.", detail);
                case "redirects": return Loc.Get("update.fail_redirects", "GitHub sent the download around too many addresses, so it was refused.");
                case "http": return Loc.Get("update.fail_http", "GitHub answered with an error ({0}).", detail);
                case "release-bad": return Loc.Get("update.fail_release", "GitHub's answer could not be read as a release.");
                case "no-digest": return Loc.Get("update.fail_no_digest", "The release lists no checksum for {0}, so it cannot be verified and was not downloaded.", detail);
                case "digest": return Loc.Get("update.fail_digest", "The downloaded file does not match the checksum the release lists, so it was thrown away.");
                case "interrupted": return Loc.Get("update.fail_interrupted", "The download stopped before the end.");
                case "too-big": return Loc.Get("update.fail_too_big", "The download is far larger than a release should be, so it was refused.");
                case "zip-unsafe": return Loc.Get("update.fail_zip", "The release's zip has an entry outside the mod's folders, so all of it was refused ({0}).", detail);
                case "zip-bad": return Loc.Get("update.fail_zip_bad", "The release's zip could not be read.");
                case "wrong-version": return Loc.Get("update.fail_version", "The mod inside the zip is not the version the release says it is.");
                case "disk": return Loc.Get("update.fail_disk", "There is not enough free disk space for the update.");
                case "denied": return Loc.Get("update.fail_denied", "Windows refused to write to the game folder. Start the game as administrator once, or install from the release page.");
                default: return Loc.Get("update.fail_unknown", "Something went wrong: {0}", detail);
            }
        }

        // --- the steps
        private void Update()
        {
            if (_flow == null)
            {
                return;
            }
            UpdateFlow.Step step = _flow.Current;
            if (step == UpdateFlow.Step.Downloading || step == UpdateFlow.Step.Unpacking)
            {
                ShowProgress();
            }
            if (step == _shown)
            {
                return;
            }
            bool wasWorking = _shown == UpdateFlow.Step.Downloading || _shown == UpdateFlow.Step.Unpacking;
            bool working = step == UpdateFlow.Step.Downloading || step == UpdateFlow.Step.Unpacking;
            _shown = step;
            if (working && wasWorking)
            {
                return;
            }
            Clear();
            switch (step)
            {
                case UpdateFlow.Step.Checking:
                    Say(Loc.Get("update.checking_title", "Checking for updates"), Loc.Get("update.checking", "Asking GitHub for the latest release…"), false);
                    Button(Loc.Get("update.cancel", "Cancel"), false, Abort);
                    break;
                case UpdateFlow.Step.UpToDate:
                    Say(Loc.Get("update.current_title", "You are up to date"), Loc.Get("update.current", "No newer release of Island UI or Island Ex than what you have."), false);
                    Button(Loc.Get("update.close", "Close"), true, Close);
                    break;
                case UpdateFlow.Step.Offer:
                    ShowOffer();
                    break;
                case UpdateFlow.Step.Downloading:
                case UpdateFlow.Step.Unpacking:
                    ShowWorking();
                    break;
                case UpdateFlow.Step.Ready:
                    ShowReady();
                    break;
                case UpdateFlow.Step.Failed:
                    Say(Loc.Get("update.failed_title", "The update stopped"), FailureText(_flow.FailKind, _flow.FailDetail), true);
                    AddText(_body, Loc.Get("update.nothing_changed", "Nothing was changed."), 18, Tan);
                    Button(Loc.Get("update.close", "Close"), true, Close);
                    break;
                case UpdateFlow.Step.Closed:
                    Close();
                    break;
            }
        }

        // the new version, its notes, and exactly what is going to be replaced, before anything
        // is downloaded
        private void ShowOffer()
        {
            _title.text = Loc.Get("update.offer_title", "Version {0} is available", _flow.Tag.TrimStart('v'));
            Notes(_body, UpdateRules.Notes(_flow.Body, 9000));

            AddText(_body, Loc.Get("update.replaces", "Update replaces:"), 18, Tan);
            foreach (UpdateFlow.Part part in _flow.Parts)
            {
                AddText(_body, Loc.Get("update.replaces_mod", "{0} {1} with {2}, in {3}", part.Mod.Name, part.Installed, part.Version,
                    string.Join(", ", Folders(part.Mod))), 18, Cream);
                foreach (string stray in part.Strays)
                {
                    AddText(_body, Loc.Get("update.replaces_stray", "and moves the extra copy {0} out of plugins", stray), 18, Cream);
                }
            }
            AddText(_body, Loc.Get("update.keeps", "Your other mods and your settings stay as they are. The current version is kept, so you can go back."), 18, Cream);

            Button(Loc.Get("update.update", "Update ({0})", UpdateRules.Size(_flow.Total)), true, () => _flow.Download());
            Button(Loc.Get("update.not_now", "Not now"), false, Close);
        }

        private static string[] Folders(UpdateRules.Mod mod)
        {
            var names = new List<string>();
            foreach (string root in mod.Roots)
            {
                names.Add(root.TrimEnd('/').Replace('/', '\\'));
            }
            return names.ToArray();
        }

        private void ShowWorking()
        {
            _title.text = Loc.Get("update.working_title", "Updating to version {0}", _flow.Tag.TrimStart('v'));
            _progress = AddText(_body, "", 18, Cream);
            var track = Box(_body, "Track", Ink);
            track.gameObject.AddComponent<LayoutElement>().preferredHeight = 14f;
            _bar = Box(track, "Bar", Tan);
            _bar.anchorMin = Vector2.zero;
            _bar.anchorMax = new Vector2(0f, 1f);
            _bar.offsetMin = _bar.offsetMax = Vector2.zero;
            Button(Loc.Get("update.cancel", "Cancel"), false, Abort);
            ShowProgress();
        }

        private void ShowProgress()
        {
            if (_progress == null)
            {
                return;
            }
            long total = Math.Max(1L, _flow.Total), done = Math.Min(_flow.Done, total);
            _progress.text = _flow.Current == UpdateFlow.Step.Unpacking
                ? Loc.Get("update.unpacking", "Checking and unpacking…")
                : Loc.Get("update.downloading", "Downloading: {0} of {1}", UpdateRules.Size(done), UpdateRules.Size(total));
            _bar.anchorMax = new Vector2((float)done / total, 1f);
        }

        private void ShowReady()
        {
            _title.text = Loc.Get("update.ready_title", "Ready: restart the game to finish");
            AddText(_body, Loc.Get("update.ready", "Version {0} is verified and waiting. It takes over the next time you start the game; until then nothing has changed, and the mod's icon shows the pending change.", _flow.Tag.TrimStart('v')), 18, Cream);
            AddText(_body, Loc.Get("update.ready_cancel", "You can still cancel it from the icon's menu."), 18, Tan);
            TitleBadge.RefreshAll();
            Button(Loc.Get("update.close", "Close"), true, Close);
        }

        private void Abort()
        {
            Close();
        }

        // however the window goes (a button, or the title scene ending under it), a step still
        // running stops with it: nothing goes on where the player cannot see it
        private void OnDestroy()
        {
            _flow?.Cancel();
        }

        private void Close()
        {
            Destroy(gameObject);
        }

        // --- building
        private static UpdatePanel Create()
        {
            Transform canvas = TitleBadge.Canvas();
            if (canvas == null)
            {
                return null;
            }
            Transform old = canvas.Find(PanelName);
            if (old != null)
            {
                Destroy(old.gameObject);
            }

            // over the whole screen, so while the window is up it is the only thing to click
            RectTransform dim = Box(canvas, PanelName, new Color(0f, 0f, 0f, 0.6f));
            dim.anchorMin = Vector2.zero;
            dim.anchorMax = Vector2.one;
            dim.offsetMin = dim.offsetMax = Vector2.zero;
            dim.GetComponent<Image>().raycastTarget = true;
            UpdatePanel panel = dim.gameObject.AddComponent<UpdatePanel>();

            RectTransform rim = Box(dim, "Rim", Ink);
            rim.anchorMin = rim.anchorMax = rim.pivot = new Vector2(0.5f, 0.5f);
            rim.sizeDelta = new Vector2(Width, 200f);
            Column(rim.gameObject, 2, 2, 0f);
            ContentSizeFitter fit = rim.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RectTransform paper = Box(rim, "Paper", Paper);
            Column(paper.gameObject, 22, 18, 12f);
            panel._title = AddText(paper, "", 26, Cream);
            RectTransform body = New(paper, "Body");
            Column(body.gameObject, 0, 0, 8f);
            panel._body = body;
            RectTransform footer = New(paper, "Footer");
            HorizontalLayoutGroup row = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleRight;
            row.spacing = 10f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            panel._footer = footer;
            return panel;
        }

        private void Clear()
        {
            _progress = null;
            foreach (Transform t in new[] { _body, _footer })
            {
                for (int i = t.childCount - 1; i >= 0; i--)
                {
                    // out of the layout now, gone at the end of the frame
                    GameObject child = t.GetChild(i).gameObject;
                    child.SetActive(false);
                    child.transform.SetParent(null, false);
                    Destroy(child);
                }
            }
        }

        private void Say(string title, string text, bool bad)
        {
            _title.text = title;
            AddText(_body, text, 20, bad ? (Color)Coral : Cream);
        }

        private void Button(string label, bool primary, Action act)
        {
            RectTransform box = Box(_footer, "Button", primary ? Tan : Lift);
            box.GetComponent<Image>().raycastTarget = true;
            HorizontalLayoutGroup pad = box.gameObject.AddComponent<HorizontalLayoutGroup>();
            pad.padding = new RectOffset(18, 18, 8, 8);
            pad.childControlWidth = pad.childControlHeight = true;
            pad.childForceExpandWidth = pad.childForceExpandHeight = false;
            AddText(box, label, 20, primary ? (Color)Ink : Cream);
            Button button = box.gameObject.AddComponent<Button>();
            ColorBlock colours = button.colors;
            colours.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            colours.selectedColor = Color.white;
            colours.colorMultiplier = 1.2f;
            button.colors = colours;
            button.onClick.AddListener(() =>
            {
                try { act(); }
                catch (Exception e) { Debug.LogWarning("Island update: " + e.Message); }
            });
        }

        // the notes in a box that scrolls: a release's can run to pages
        private static void Notes(Transform parent, List<UpdateRules.Block> blocks)
        {
            RectTransform frame = Box(parent, "NotesFrame", Ink);
            frame.gameObject.AddComponent<LayoutElement>().preferredHeight = 340f;
            HorizontalLayoutGroup row = frame.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(4, 4, 4, 4);
            row.spacing = 4f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;

            RectTransform viewport = New(frame, "Viewport");
            viewport.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            viewport.gameObject.AddComponent<RectMask2D>();
            Image catcher = viewport.gameObject.AddComponent<Image>();    // the wheel needs a target
            catcher.color = Color.clear;

            RectTransform content = New(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            Column(content.gameObject, 8, 6, 5f);
            ContentSizeFitter fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            foreach (UpdateRules.Block block in blocks)
            {
                AddText(content, block.Text, block.Heading ? 20 : 17, block.Heading ? (Color)Tan : Cream);
            }

            RectTransform track = Box(frame, "Scrollbar", Paper);
            track.GetComponent<Image>().raycastTarget = true;
            track.gameObject.AddComponent<LayoutElement>().preferredWidth = 12f;
            RectTransform handle = Box(track, "Handle", Lift);
            handle.GetComponent<Image>().raycastTarget = true;
            handle.offsetMin = handle.offsetMax = Vector2.zero;
            Scrollbar bar = track.gameObject.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.handleRect = handle;
            bar.targetGraphic = handle.GetComponent<Image>();

            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            // Canvas layout runs after this frame's Start; the notes begin at their top
            scroll.verticalNormalizedPosition = 1f;
        }

        private static RectTransform New(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static RectTransform Box(Transform parent, string name, Color colour)
        {
            RectTransform rect = New(parent, name);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return rect;
        }

        private static void Column(GameObject go, int padX, int padY, float spacing)
        {
            VerticalLayoutGroup column = go.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(padX, padX, padY, padY);
            column.spacing = spacing;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
        }

        private static Text AddText(Transform parent, string text, int size, Color colour)
        {
            RectTransform rect = New(parent, "Text");
            Text label = rect.gameObject.AddComponent<Text>();
            label.font = TitleBadge.TitleFont();
            label.fontSize = size;
            label.color = colour;
            label.text = text;
            label.supportRichText = false;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }
    }
}
