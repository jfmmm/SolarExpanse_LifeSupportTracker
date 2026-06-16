#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.UI.Windows.Elements.ObjectInfoElements;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using CustomUpdate;
using Game;
using Game.Info;
using Game.ObjectInfoDataScripts;
using Game.UI;
using Manager;
using ScriptableObjectScripts;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using LifeSupportTracker.Patches;

namespace LifeSupportTracker.UI
{
    internal static class SupplyTrackerInjector
    {
        private static readonly FieldInfo FieldShowBtn =
            typeof(NotificationManager).GetField("showNotificationHistory",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo FieldHistoryGO =
            typeof(NotificationManager).GetField("notificationHistory",
                BindingFlags.Instance | BindingFlags.NonPublic);

        internal static void Inject(NotificationManager nm, ManualLogSource log, SupplyTrackerConfig config)
        {
            try
            {
                Button showBtn = FieldShowBtn?.GetValue(nm) as Button;
                if (showBtn == null) { log.LogError("[LST] showNotificationHistory not found"); return; }

                GameObject historyGO = FieldHistoryGO?.GetValue(nm) as GameObject;
                if (historyGO == null) { log.LogError("[LST] notificationHistory GO not found"); return; }

                RectTransform showBtnRT = showBtn.GetComponent<RectTransform>();

                Canvas btnCanvas = showBtn.GetComponentInParent<Canvas>();
                if (btnCanvas == null) { log.LogError("[LST] could not find canvas"); return; }
                log.LogInfo($"[LST] btnCanvas='{btnCanvas.name}' sortOrder={btnCanvas.sortingOrder}");

                log.LogInfo($"[LST] btnCanvas has {btnCanvas.transform.childCount} direct children:");
                for (int ci = 0; ci < btnCanvas.transform.childCount; ci++)
                    log.LogInfo($"[LST]   [{ci}] {btnCanvas.transform.GetChild(ci).name}");

                TMP_FontAsset fontAsset = FindFontAsset(nm, historyGO, log);

                // ── Clone notification history as our panel ──────────────────────────
                GameObject panelGO = UnityEngine.Object.Instantiate(historyGO, btnCanvas.transform);
                panelGO.name = "modLifeSupportPanel";
                panelGO.transform.SetAsLastSibling();
                RectTransform panelRT = panelGO.GetComponent<RectTransform>();

                panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
                panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
                panelRT.pivot            = new Vector2(0f, 1f);
                panelRT.sizeDelta        = new Vector2(750f, 300f);
                panelRT.anchoredPosition = new Vector2(-9999f, -9999f);

                LayoutElement panelLE = panelGO.AddComponent<LayoutElement>();
                panelLE.ignoreLayout = true;

                // Steal background sprite before destroying children
                Image bgSource = null;
                foreach (Image img in panelGO.GetComponentsInChildren<Image>(includeInactive: true))
                    if (img.sprite != null) { bgSource = img; break; }
                log.LogInfo($"[LST] bgSource='{bgSource?.name}' sprite='{bgSource?.sprite?.name}'");

                Image panelBg = panelGO.GetComponent<Image>() ?? panelGO.AddComponent<Image>();
                if (bgSource != null)
                {
                    panelBg.sprite   = bgSource.sprite;
                    panelBg.color    = bgSource.color;
                    panelBg.type     = bgSource.type;
                    panelBg.material = bgSource.material;
                }
                else panelBg.color = new Color(0.08f, 0.08f, 0.12f, 0.95f);
                panelBg.raycastTarget = true;

                for (int i = panelGO.transform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(panelGO.transform.GetChild(i).gameObject);

                foreach (CanvasGroup cg in panelGO.GetComponents<CanvasGroup>())
                { cg.interactable = true; cg.blocksRaycasts = true; }

                foreach (ScrollRect sr in panelGO.GetComponents<ScrollRect>())
                    UnityEngine.Object.DestroyImmediate(sr);
                foreach (LayoutGroup lg in panelGO.GetComponents<LayoutGroup>())
                    UnityEngine.Object.DestroyImmediate(lg);
                ContentSizeFitter existingCSF = panelGO.GetComponent<ContentSizeFitter>();
                if (existingCSF != null) UnityEngine.Object.DestroyImmediate(existingCSF);

                panelRT.sizeDelta = new Vector2(750f, 300f);

                // ── Tab bar (24px, inset 8px from edges, 8px below top border) ───────
                const float TabH = 24f;
                Color tabActive   = new Color(0.20f, 0.35f, 0.40f, 1f);
                Color tabInactive = new Color(0.10f, 0.15f, 0.18f, 1f);

                GameObject tabBarGO = new GameObject("TabBar", typeof(RectTransform));
                tabBarGO.transform.SetParent(panelGO.transform, false);
                RectTransform tabBarRT = tabBarGO.GetComponent<RectTransform>();
                tabBarRT.anchorMin        = new Vector2(0f, 1f);
                tabBarRT.anchorMax        = new Vector2(1f, 1f);
                tabBarRT.pivot            = new Vector2(0.5f, 1f);
                tabBarRT.sizeDelta        = new Vector2(-16f, TabH);
                tabBarRT.anchoredPosition = new Vector2(0f, -8f);

                HorizontalLayoutGroup tabHLG = tabBarGO.AddComponent<HorizontalLayoutGroup>();
                tabHLG.childControlWidth      = true;
                tabHLG.childForceExpandWidth  = true;
                tabHLG.childControlHeight     = true;
                tabHLG.childForceExpandHeight = true;
                tabHLG.spacing = 4f;

                (Button statusTabBtn, Image statusTabImg)   = MakeTabButton(tabBarGO.transform, fontAsset, "STATUS",   tabActive);
                (Button settingsTabBtn, Image settingsTabImg) = MakeTabButton(tabBarGO.transform, fontAsset, "ALERT THRESHOLDS", tabInactive);

                // ── Scroll viewport — top offset = 8 border + TabH + 4 gap ─────────
                GameObject viewportGO = new GameObject("ScrollViewport", typeof(RectTransform));
                viewportGO.transform.SetParent(panelGO.transform, false);
                viewportGO.transform.SetAsLastSibling();
                RectTransform viewportRT = viewportGO.GetComponent<RectTransform>();
                viewportRT.anchorMin = Vector2.zero;
                viewportRT.anchorMax = Vector2.one;
                viewportRT.pivot     = new Vector2(0.5f, 0.5f);
                viewportRT.offsetMin = new Vector2(8f, 8f);
                viewportRT.offsetMax = new Vector2(-22f, -(8f + TabH + 4f));
                viewportGO.AddComponent<RectMask2D>();

                // STATUS content (active by default)
                GameObject contentGO = MakeScrollContent("ScrollContent", viewportGO.transform, 1f, 4);
                RectTransform contentRT = contentGO.GetComponent<RectTransform>();

                // SETTINGS content (inactive by default)
                GameObject settingsContentGO = MakeScrollContent("SettingsContent", viewportGO.transform, 2f, 4);
                RectTransform settingsContentRT = settingsContentGO.GetComponent<RectTransform>();
                settingsContentGO.SetActive(false);

                // ── Vertical scrollbar ───────────────────────────────────────────────
                GameObject scrollbarGO = new GameObject("Scrollbar", typeof(RectTransform));
                scrollbarGO.transform.SetParent(panelGO.transform, false);
                RectTransform scrollbarRT = scrollbarGO.GetComponent<RectTransform>();
                // topPad = 8 border + TabH(24) + 4 gap = 36; botPad = 8
                // sizeDelta.y = -(topPad + botPad) = -44
                // anchoredPosition.y = -(topPad - botPad) / 2 = -14  (shifts centre down)
                scrollbarRT.anchorMin        = new Vector2(1f, 0f);
                scrollbarRT.anchorMax        = new Vector2(1f, 1f);
                scrollbarRT.pivot            = new Vector2(1f, 0.5f);
                scrollbarRT.sizeDelta        = new Vector2(6f, -(8f + TabH + 4f + 8f));
                scrollbarRT.anchoredPosition = new Vector2(-8f, -(8f + TabH + 4f - 8f) / 2f);
                Image scrollbarBg = scrollbarGO.AddComponent<Image>();
                Scrollbar scrollbar = scrollbarGO.AddComponent<Scrollbar>();
                scrollbar.direction = Scrollbar.Direction.BottomToTop;

                GameObject slidingAreaGO = new GameObject("SlidingArea", typeof(RectTransform));
                slidingAreaGO.transform.SetParent(scrollbarGO.transform, false);
                RectTransform slidingAreaRT = slidingAreaGO.GetComponent<RectTransform>();
                slidingAreaRT.anchorMin        = Vector2.zero;
                slidingAreaRT.anchorMax        = Vector2.one;
                slidingAreaRT.sizeDelta        = Vector2.zero;
                slidingAreaRT.anchoredPosition = Vector2.zero;

                GameObject handleGO = new GameObject("Handle", typeof(RectTransform));
                handleGO.transform.SetParent(slidingAreaGO.transform, false);
                RectTransform handleRT = handleGO.GetComponent<RectTransform>();
                handleRT.anchorMin = Vector2.zero;
                handleRT.anchorMax = Vector2.one;
                handleRT.sizeDelta = Vector2.zero;
                Image handleImg = handleGO.AddComponent<Image>();

                scrollbar.handleRect    = handleRT;
                scrollbar.targetGraphic = handleImg;
                CopyGameScrollbarStyle(scrollbarBg, handleImg, log);

                ScrollRect scrollRect = panelGO.AddComponent<ScrollRect>();
                scrollRect.viewport                    = viewportRT;
                scrollRect.content                     = contentRT;
                scrollRect.verticalScrollbar           = scrollbar;
                scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
                scrollRect.horizontal        = false;
                scrollRect.vertical          = true;
                scrollRect.scrollSensitivity = 30f;
                scrollRect.movementType      = ScrollRect.MovementType.Clamped;

                // ── Bottom resize handle ─────────────────────────────────────────────
                GameObject resizeHandleGO = new GameObject("ResizeHandle", typeof(RectTransform));
                resizeHandleGO.transform.SetParent(panelGO.transform, false);
                RectTransform resizeRT = resizeHandleGO.GetComponent<RectTransform>();
                resizeRT.anchorMin        = new Vector2(0f, 0f);
                resizeRT.anchorMax        = new Vector2(1f, 0f);
                resizeRT.pivot            = new Vector2(0.5f, 1f);
                resizeRT.sizeDelta        = new Vector2(0f, 10f);
                resizeRT.anchoredPosition = Vector2.zero;
                resizeHandleGO.AddComponent<Image>().color = Color.clear;
                resizeHandleGO.AddComponent<ResizeHandle>().PanelRT = panelRT;

                panelGO.SetActive(false);
                PauseScreenEscPatch.PanelGO = panelGO;

                SupplyTrackerPanel tracker = panelGO.AddComponent<SupplyTrackerPanel>();
                tracker.ContentParent     = contentGO.transform;
                tracker.FontAsset         = fontAsset;
                tracker.TrackerLog        = log;
                tracker.PanelRT           = panelRT;
                tracker.Config            = config;
                tracker.SettingsContentGO = settingsContentGO;
                tracker.SettingsContentRT = settingsContentRT;
                tracker.StatusContentRT   = contentRT;
                tracker.ScrollRectRef     = scrollRect;
                tracker.StatusTabImg      = statusTabImg;
                tracker.SettingsTabImg    = settingsTabImg;
                tracker.TabActiveColor    = tabActive;
                tracker.TabInactiveColor  = tabInactive;

                // ── Floating draggable indicator button ──────────────────────────────
                GameObject indicatorGO = new GameObject("modLifeSupportButton", typeof(RectTransform));
                indicatorGO.transform.SetParent(btnCanvas.transform, false);
                indicatorGO.transform.SetAsLastSibling();

                LayoutElement indicatorLE = indicatorGO.AddComponent<LayoutElement>();
                indicatorLE.ignoreLayout = true;

                RectTransform indicatorRT = indicatorGO.GetComponent<RectTransform>();
                indicatorRT.anchorMin        = new Vector2(0.5f, 0.5f);
                indicatorRT.anchorMax        = new Vector2(0.5f, 0.5f);
                indicatorRT.pivot            = new Vector2(0f, 1f);
                indicatorRT.sizeDelta        = new Vector2(150f, 30f);
                indicatorRT.anchoredPosition = new Vector2(-9999f, -9999f);

                Image bg = indicatorGO.AddComponent<Image>();
                Image origBtnImg = showBtn.GetComponent<Image>();
                if (origBtnImg != null) { bg.sprite = origBtnImg.sprite; bg.type = origBtnImg.type; bg.color = origBtnImg.color; }
                else bg.color = new Color(0.15f, 0.15f, 0.2f, 0.9f);

                TextMeshProUGUI indicatorLabel = MakeButtonLabel(indicatorGO, fontAsset);

                DraggableMover mover = indicatorGO.AddComponent<DraggableMover>();
                mover.Bg          = bg;
                mover.NormalColor = bg.color;
                mover.HoverColor  = bg.color * 1.3f;
                mover.PressColor  = bg.color * 0.7f;

                mover.OnClick = () =>
                {
                    bool open = panelGO.activeSelf;
                    if (!open)
                    {
                        panelGO.SetActive(true);
                        panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
                        panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
                        panelRT.pivot            = new Vector2(0f, 1f);
                        panelRT.anchoredPosition = new Vector2(
                            indicatorRT.anchoredPosition.x,
                            indicatorRT.anchoredPosition.y - indicatorRT.sizeDelta.y - 4f);
                        scrollRect.verticalNormalizedPosition = 1f;
                        log.LogInfo($"[LST] open: btn={indicatorRT.anchoredPosition} panel={panelRT.anchoredPosition}");
                        tracker.RefreshRows();
                    }
                    else
                    {
                        panelGO.SetActive(false);
                    }
                };

                // Tab buttons wired after tracker exists
                statusTabBtn.onClick.AddListener(()  => tracker.ShowStatusTab());
                settingsTabBtn.onClick.AddListener(() => tracker.ShowSettingsTab());

                mover.PanelRT   = panelRT;
                mover.PanelGO   = panelGO;
                mover.ShowBtnRT = showBtnRT;
                mover.Log       = log;

                tracker.IndicatorLabel = indicatorLabel;
                tracker.IndicatorRT    = indicatorRT;
                tracker.Mover          = mover;
                mover.FlashLabel       = indicatorLabel;

                indicatorGO.AddComponent<TrackerUpdater>().Tracker = tracker;

                log.LogInfo("[LST] Injection complete");
            }
            catch (Exception e)
            {
                log.LogError($"[LST] Inject exception: {e}");
            }
        }

        // Creates a standard scroll content GO (VLG + CSF, anchored to viewport top).
        private static GameObject MakeScrollContent(string name, Transform parent, float spacing, int padding)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.sizeDelta = Vector2.zero;

            VerticalLayoutGroup vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight     = true;
            vlg.childControlWidth      = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth  = true;
            vlg.spacing = spacing;
            vlg.padding = new RectOffset(padding, padding, padding, padding);

            ContentSizeFitter csf = go.AddComponent<ContentSizeFitter>();
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            return go;
        }

        private static (Button btn, Image img) MakeTabButton(Transform parent, TMP_FontAsset font, string label, Color color)
        {
            GameObject go = new GameObject($"Tab_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image img = go.AddComponent<Image>();
            img.color = color;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition    = Selectable.Transition.None; // we manage color manually

            GameObject labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(go.transform, false);
            RectTransform lrt = labelGO.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.sizeDelta = Vector2.zero;
            TextMeshProUGUI tmp = labelGO.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text          = label;
            tmp.fontSize      = 10f;
            tmp.fontStyle     = FontStyles.Bold;
            tmp.alignment     = TextAlignmentOptions.Center;
            tmp.color         = Color.white;
            tmp.raycastTarget = false;

            return (btn, img);
        }

        private static TextMeshProUGUI MakeButtonLabel(GameObject parent, TMP_FontAsset font)
        {
            GameObject labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(parent.transform, false);
            RectTransform lrt = labelGO.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.sizeDelta = Vector2.zero;
            TextMeshProUGUI tmp = labelGO.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text          = "<color=#44BB44>●</color>  LIFE SUPPORT";
            tmp.fontSize      = 11f;
            tmp.alignment     = TextAlignmentOptions.Center;
            tmp.color         = Color.white;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static void CopyGameScrollbarStyle(Image track, Image handle, ManualLogSource log)
        {
            Scrollbar src = null;
            foreach (Scrollbar sb in Resources.FindObjectsOfTypeAll<Scrollbar>())
            {
                if (sb.name == "Scrollbar" && sb.handleRect != null) continue;
                if (sb.handleRect != null) { src = sb; break; }
            }
            if (src == null) { log.LogWarning("[LST] No game Scrollbar — using fallback"); ApplyFallbackScrollbarStyle(track, handle); return; }
            Image srcTrack  = src.GetComponent<Image>();
            Image srcHandle = src.handleRect.GetComponent<Image>();
            if (srcTrack  != null) { track.sprite  = srcTrack.sprite;  track.color  = srcTrack.color;  track.type  = srcTrack.type; }
            if (srcHandle != null) { handle.sprite = srcHandle.sprite; handle.color = srcHandle.color; handle.type = srcHandle.type; }
            log.LogInfo($"[LST] Scrollbar style from '{src.name}' on '{src.transform.root.name}'");
        }

        private static void ApplyFallbackScrollbarStyle(Image track, Image handle)
        {
            track.color  = new Color(0.06f, 0.12f, 0.14f, 0.9f);
            handle.color = new Color(0.05f, 0.62f, 0.68f, 0.9f);
        }

        private static TMP_FontAsset FindFontAsset(NotificationManager nm, GameObject historyGO, ManualLogSource log)
        {
            TextMeshProUGUI src = historyGO.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (src?.font != null) { log.LogInfo($"[LST] font '{src.font.name}'"); return src.font; }
            try
            {
                var prefabField = typeof(NotificationManager).GetField("notificationUIPrefab",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var prefab = prefabField?.GetValue(nm);
                if (prefab != null)
                {
                    var textField = prefab.GetType().GetField("text", BindingFlags.Instance | BindingFlags.NonPublic);
                    src = textField?.GetValue(prefab) as TextMeshProUGUI;
                    if (src?.font != null) { log.LogInfo($"[LST] font from prefab '{src.font.name}'"); return src.font; }
                }
            }
            catch (Exception e) { log.LogWarning($"[LST] font fallback: {e.Message}"); }
            log.LogWarning("[LST] No font found");
            return null;
        }
    }

    // ── Draggable floating button ─────────────────────────────────────────────
    internal class DraggableMover : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal Action OnClick;

        internal Image Bg;
        internal Color NormalColor;
        internal Color HoverColor;
        internal Color PressColor;

        internal TextMeshProUGUI FlashLabel;
        internal bool IsCritical;
        private float _flashTimer;
        private bool _flashOn = true;

        internal RectTransform ShowBtnRT;
        internal ManualLogSource Log;
        internal RectTransform PanelRT;
        internal GameObject PanelGO;

        private RectTransform _rt;
        private Canvas _canvas;
        private RectTransform _canvasRT;
        private Vector2 _dragStartAnchoredPos;
        private Vector2 _pressScreenPos;
        private Vector2 _lastCanvasSize;
        private Vector2 _normalizedPos;
        private bool _normalizedPosSet;

        private void Awake()
        {
            _rt       = GetComponent<RectTransform>();
            _canvas   = GetComponentInParent<Canvas>();
            _canvasRT = _canvas?.GetComponent<RectTransform>();
        }

        private IEnumerator Start()
        {
            yield return null;
            yield return null;
            PositionNextToNotificationButton();
        }

        private void Update()
        {
            if (FlashLabel != null && IsCritical)
            {
                _flashTimer += Time.deltaTime;
                if (_flashTimer >= 0.5f)
                {
                    _flashTimer = 0f;
                    _flashOn = !_flashOn;
                    FlashLabel.text = _flashOn
                        ? "<color=#FF3333>●</color>  LIFE SUPPORT"
                        : "<color=#1A0000>●</color>  LIFE SUPPORT";
                }
            }

            if (_canvasRT != null)
            {
                Vector2 sz = _canvasRT.rect.size;
                if (sz != _lastCanvasSize)
                {
                    _lastCanvasSize = sz;
                    RestoreFromNormalizedPos();
                    RepositionPanel();
                }
            }
        }

        private void PositionNextToNotificationButton()
        {
            if (ShowBtnRT == null || _rt == null) return;
            Camera cam = _canvas != null && _canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : _canvas?.worldCamera;

            Vector3[] corners = new Vector3[4];
            ShowBtnRT.GetWorldCorners(corners);

            Vector2 btnTopLeft;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRT, new Vector2(corners[1].x, corners[1].y), cam, out btnTopLeft))
            {
                Log?.LogWarning("[LST] RectTransformUtility failed — keeping parked position");
                return;
            }

            float x = btnTopLeft.x - 10f - _rt.sizeDelta.x;
            _rt.anchoredPosition = new Vector2(x, btnTopLeft.y - 5f);
            StoreNormalizedPos();
            Log?.LogInfo($"[LST] indicator at {_rt.anchoredPosition} (btnTopLeft={btnTopLeft})");
        }

        public void OnPointerEnter(PointerEventData e) { if (Bg) Bg.color = HoverColor; }
        public void OnPointerExit(PointerEventData e)  { if (Bg) Bg.color = NormalColor; }

        public void OnPointerDown(PointerEventData e)
        {
            _pressScreenPos = e.position;
            if (Bg) Bg.color = PressColor;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (Bg) Bg.color = HoverColor;
            if (Vector2.Distance(e.position, _pressScreenPos) < EventSystem.current.pixelDragThreshold)
                OnClick?.Invoke();
        }

        public void OnBeginDrag(PointerEventData e)
        {
            _dragStartAnchoredPos = _rt.anchoredPosition;
        }

        public void OnDrag(PointerEventData e)
        {
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            _rt.anchoredPosition = _dragStartAnchoredPos + (e.position - _pressScreenPos) / scale;
            Clamp();
            RepositionPanel();
        }

        public void OnEndDrag(PointerEventData e)
        {
            Clamp();
            StoreNormalizedPos();
            RepositionPanel();
            if (Bg) Bg.color = NormalColor;
        }

        private void StoreNormalizedPos()
        {
            if (_canvasRT == null) return;
            Rect cr = _canvasRT.rect;
            if (cr.xMax <= 0f || cr.yMax <= 0f) return;
            _normalizedPos = new Vector2(_rt.anchoredPosition.x / cr.xMax, _rt.anchoredPosition.y / cr.yMax);
            _normalizedPosSet = true;
        }

        private void RestoreFromNormalizedPos()
        {
            if (_canvasRT == null) return;
            if (_normalizedPosSet)
            {
                Rect cr = _canvasRT.rect;
                _rt.anchoredPosition = new Vector2(_normalizedPos.x * cr.xMax, _normalizedPos.y * cr.yMax);
            }
            Clamp();
        }

        private void Clamp()
        {
            if (_canvasRT == null) return;
            Rect cr   = _canvasRT.rect;
            Vector2 s = _rt.sizeDelta;
            Vector2 p = _rt.anchoredPosition;
            p.x = Mathf.Clamp(p.x, cr.xMin,        cr.xMax - s.x);
            p.y = Mathf.Clamp(p.y, cr.yMin + s.y,  cr.yMax);
            _rt.anchoredPosition = p;
        }

        private void RepositionPanel()
        {
            if (PanelGO == null || !PanelGO.activeSelf || PanelRT == null) return;
            PanelRT.anchoredPosition = new Vector2(
                _rt.anchoredPosition.x,
                _rt.anchoredPosition.y - _rt.sizeDelta.y - 4f);
        }
    }

    // ── Supply tracker panel ──────────────────────────────────────────────────
    internal class SupplyTrackerPanel : MonoBehaviour
    {
        internal Transform ContentParent;
        internal TMP_FontAsset FontAsset;
        internal ManualLogSource TrackerLog;
        internal TextMeshProUGUI IndicatorLabel;
        internal RectTransform PanelRT;
        internal RectTransform IndicatorRT;
        internal DraggableMover Mover;
        internal SupplyTrackerConfig Config;

        // Settings tab
        internal GameObject SettingsContentGO;
        internal RectTransform SettingsContentRT;
        internal RectTransform StatusContentRT;
        internal ScrollRect ScrollRectRef;
        internal Image StatusTabImg;
        internal Image SettingsTabImg;
        internal Color TabActiveColor;
        internal Color TabInactiveColor;

        private List<string> _lastActiveBodyNames = new List<string>();

        private const float ColPop    = 52f;
        private const float ColSupply = 72f;
        private const float ColProd   = 72f;
        private const float ColCons   = 72f;
        private const float ColDays   = 62f;
        private const float ColIncome = 72f;

        // Reflection handles stable/beta compatibility: direct IL references to beta-only
        // properties would cause MissingMethodException during JIT even if guarded by an if.
        private static readonly PropertyInfo _propHumanDailyMoneyProduction =
            typeof(Economic).GetProperty("HumanDailyMoneyProduction");
        private static readonly PropertyInfo _propHumanDailyMoneyProductionMultiplier =
            typeof(ObjectInfo).GetProperty("HumanDailyMoneyProductionMultiplier");
        private static readonly bool _supportsColonistIncome =
            _propHumanDailyMoneyProduction != null && _propHumanDailyMoneyProductionMultiplier != null;

        internal const float RefreshInterval = 5.0f;

        // ── Persistent status-tab UI rows ─────────────────────────────────────
        private bool _headerBuilt;
        private GameObject _titleRowGO;
        private TextMeshProUGUI _titleLbl;
        private GameObject _headerRowGO;
        private GameObject _headerSepGO;
        private GameObject _emptyMsgGO;
        private TextMeshProUGUI _emptyMsgLbl;
        private GameObject _shipSepGO;
        private GameObject _shipLblGO;
        private readonly Dictionary<string, StatusRowCache> _colonyRowCache  = new Dictionary<string, StatusRowCache>();
        private readonly Dictionary<string, StatusRowCache> _vehicleRowCache = new Dictionary<string, StatusRowCache>();

        private class StatusRowCache
        {
            public GameObject GO;
            public TextMeshProUGUI NameCol, PopCol, SupplyCol, ProdCol, ConsCol, DaysCol, IncomeCol;
            public Button Btn;
            public object BoundIdentity;
        }

        private enum Severity { OK, Warning, Critical }

        // ── Tab switching ─────────────────────────────────────────────────────

        internal void ShowStatusTab()
        {
            SettingsContentGO.SetActive(false);
            ContentParent.gameObject.SetActive(true);
            ScrollRectRef.content = StatusContentRT;
            ScrollRectRef.verticalNormalizedPosition = 1f;
            StatusTabImg.color   = TabActiveColor;
            SettingsTabImg.color = TabInactiveColor;
        }

        internal void ShowSettingsTab()
        {
            ContentParent.gameObject.SetActive(false);
            SettingsContentGO.SetActive(true);
            ScrollRectRef.content = SettingsContentRT;
            ScrollRectRef.verticalNormalizedPosition = 1f;
            StatusTabImg.color   = TabInactiveColor;
            SettingsTabImg.color = TabActiveColor;
            RebuildSettingsContent();
        }

        // ── Settings content builder ──────────────────────────────────────────

        private void RebuildSettingsContent()
        {
            if (_lastActiveBodyNames.Count > 0)
                Config.PruneToActive(_lastActiveBodyNames);

            for (int i = SettingsContentGO.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(SettingsContentGO.transform.GetChild(i).gameObject);

            AddTitleRow("ALERT THRESHOLDS", SettingsContentGO.transform);

            // Global default row — editing it rebuilds settings so per-body rows reflect new defaults
            var (dw, dc) = Config.Defaults;
            AddSettingsRow(SettingsContentGO.transform, "DEFAULT (all bodies)",
                dw, dc, isDefault: true,
                onChanged: (w, c) => { Config.SetDefaults(w, c); RebuildSettingsContent(); });

            AddSettingsDivider();

            if (_lastActiveBodyNames.Count == 0)
            {
                AddSettingsMessage("No active colonies found. Open the Status tab first.");
                return;
            }

            foreach (string name in _lastActiveBodyNames)
            {
                var (w, c) = Config.GetThresholds(name);
                string key = name;
                AddSettingsRow(SettingsContentGO.transform, name,
                    w, c, isDefault: false,
                    onChanged: (warn, crit) => Config.SetBody(key, warn, crit));
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(SettingsContentRT);
        }

        private void AddSettingsRow(Transform parent, string label,
            double warnDays, double critDays, bool isDefault,
            Action<double, double> onChanged)
        {
            int warnYrs = (int)(warnDays / 365);
            int warnD   = (int)(warnDays % 365);
            int critYrs = (int)(critDays / 365);
            int critD   = (int)(critDays % 365);

            GameObject rowGO = new GameObject($"SRow_{label}", typeof(RectTransform));
            rowGO.transform.SetParent(parent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 28f;

            HorizontalLayoutGroup hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth      = true;
            hlg.childForceExpandWidth  = false;
            hlg.childControlHeight     = true;
            hlg.childForceExpandHeight = true;
            hlg.spacing = 4f;
            hlg.padding = new RectOffset(6, 6, 2, 2);

            Color nameColor = isDefault ? new Color(1f, 0.82f, 0.35f) : Color.white;
            Color grayLbl   = new Color(0.7f, 0.7f, 0.7f);
            Color dimLbl    = new Color(0.5f, 0.5f, 0.5f);

            MakeSettingsLabel(rowGO.transform, label,    0f, 1f, TextAlignmentOptions.MidlineLeft,  nameColor);
            MakeSettingsLabel(rowGO.transform, "WARN",  55f, 0f, TextAlignmentOptions.MidlineRight, grayLbl);
            TMP_InputField warnYrsField = MakeInputField(rowGO.transform, warnYrs.ToString(), 36f);
            MakeSettingsLabel(rowGO.transform, "yrs",   22f, 0f, TextAlignmentOptions.MidlineLeft,  dimLbl);
            TMP_InputField warnDaysField = MakeInputField(rowGO.transform, warnD.ToString(), 36f);
            MakeSettingsLabel(rowGO.transform, "d",     12f, 0f, TextAlignmentOptions.MidlineLeft,  dimLbl);

            MakeSettingsLabel(rowGO.transform, "CRIT",  50f, 0f, TextAlignmentOptions.MidlineRight, grayLbl);
            TMP_InputField critYrsField = MakeInputField(rowGO.transform, critYrs.ToString(), 36f);
            MakeSettingsLabel(rowGO.transform, "yrs",   22f, 0f, TextAlignmentOptions.MidlineLeft,  dimLbl);
            TMP_InputField critDaysField = MakeInputField(rowGO.transform, critD.ToString(), 36f);
            MakeSettingsLabel(rowGO.transform, "d",     12f, 0f, TextAlignmentOptions.MidlineLeft,  dimLbl);

            int[] wyArr = { warnYrs };
            int[] wdArr = { warnD };
            int[] cyArr = { critYrs };
            int[] cdArr = { critD };

            void ApplyChange()
            {
                int w = wyArr[0] * 365 + wdArr[0];
                int c = cyArr[0] * 365 + cdArr[0];
                if (w < 0) w = 0;
                if (c < 0) c = 0;
                if (w < c) w = c;
                wyArr[0] = w / 365; wdArr[0] = w % 365;
                cyArr[0] = c / 365; cdArr[0] = c % 365;
                warnYrsField.text  = wyArr[0].ToString();
                warnDaysField.text = wdArr[0].ToString();
                critYrsField.text  = cyArr[0].ToString();
                critDaysField.text = cdArr[0].ToString();
                onChanged(w, c);
            }

            warnYrsField.onEndEdit.AddListener(val =>
            {
                if (int.TryParse(val, out int y) && y >= 0) wyArr[0] = y;
                else warnYrsField.text = wyArr[0].ToString();
                ApplyChange();
            });

            warnDaysField.onEndEdit.AddListener(val =>
            {
                if (int.TryParse(val, out int d) && d >= 0) wdArr[0] = d;
                else warnDaysField.text = wdArr[0].ToString();
                ApplyChange();
            });

            critYrsField.onEndEdit.AddListener(val =>
            {
                if (int.TryParse(val, out int y) && y >= 0) cyArr[0] = y;
                else critYrsField.text = cyArr[0].ToString();
                ApplyChange();
            });

            critDaysField.onEndEdit.AddListener(val =>
            {
                if (int.TryParse(val, out int d) && d >= 0) cdArr[0] = d;
                else critDaysField.text = cdArr[0].ToString();
                ApplyChange();
            });
        }

        private void MakeSettingsLabel(Transform parent, string text, float preferred, float flexible,
            TextAlignmentOptions align, Color color)
        {
            GameObject go = new GameObject("SLbl", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = preferred;
            le.flexibleWidth  = flexible;
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) tmp.font = FontAsset;
            tmp.text               = text;
            tmp.fontSize           = 11f;
            tmp.color              = color;
            tmp.alignment          = align;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget      = false;
        }

        private TMP_InputField MakeInputField(Transform parent, string initialValue, float width)
        {
            GameObject go = new GameObject("InputField", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredWidth = width;

            go.AddComponent<Image>().color = new Color(0.08f, 0.12f, 0.16f, 1f);

            TMP_InputField field = go.AddComponent<TMP_InputField>();
            field.contentType = TMP_InputField.ContentType.IntegerNumber;

            // Text Area (clips text inside the field)
            GameObject areaGO = new GameObject("Text Area", typeof(RectTransform));
            areaGO.transform.SetParent(go.transform, false);
            RectTransform areaRT = areaGO.GetComponent<RectTransform>();
            areaRT.anchorMin = Vector2.zero;
            areaRT.anchorMax = Vector2.one;
            areaRT.offsetMin = new Vector2(4f, 2f);
            areaRT.offsetMax = new Vector2(-4f, -2f);
            areaGO.AddComponent<RectMask2D>();

            // Placeholder
            GameObject phGO = new GameObject("Placeholder", typeof(RectTransform));
            phGO.transform.SetParent(areaGO.transform, false);
            StretchFill(phGO.GetComponent<RectTransform>());
            TextMeshProUGUI phTMP = phGO.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) phTMP.font = FontAsset;
            phTMP.text               = "0";
            phTMP.fontSize           = 11f;
            phTMP.color              = new Color(0.4f, 0.4f, 0.4f);
            phTMP.alignment          = TextAlignmentOptions.Center;
            phTMP.enableWordWrapping = false;

            // Text
            GameObject txtGO = new GameObject("Text", typeof(RectTransform));
            txtGO.transform.SetParent(areaGO.transform, false);
            StretchFill(txtGO.GetComponent<RectTransform>());
            TextMeshProUGUI txtTMP = txtGO.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) txtTMP.font = FontAsset;
            txtTMP.text               = initialValue;
            txtTMP.fontSize           = 11f;
            txtTMP.color              = Color.white;
            txtTMP.alignment          = TextAlignmentOptions.Center;
            txtTMP.enableWordWrapping = false;

            field.textViewport      = areaRT;
            field.textComponent    = txtTMP;
            field.placeholder      = phTMP;
            field.text             = initialValue;
            field.caretWidth       = 2;
            field.customCaretColor = true;
            field.caretColor       = Color.white;
            field.selectionColor   = new Color(0.2f, 0.4f, 0.8f, 0.45f);
            field.interactable     = true;
            // Cycle enabled so TMP reinitializes its caret renderer with the
            // now-wired textViewport/textComponent (Awake fired before we set them).
            field.enabled = false;
            field.enabled = true;

            return field;
        }

        private void AddSettingsDivider()
        {
            GameObject go = new GameObject("SDivider", typeof(RectTransform));
            go.transform.SetParent(SettingsContentGO.transform, false);
            go.AddComponent<LayoutElement>().preferredHeight = 1f;
            go.AddComponent<Image>().color = new Color(0.35f, 0.35f, 0.35f, 1f);
        }

        private void AddSettingsMessage(string text)
        {
            GameObject go = new GameObject("SMsg", typeof(RectTransform));
            go.transform.SetParent(SettingsContentGO.transform, false);
            go.AddComponent<LayoutElement>().preferredHeight = 20f;
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) tmp.font = FontAsset;
            tmp.text               = text;
            tmp.fontSize           = 11f;
            tmp.color              = new Color(0.55f, 0.55f, 0.55f);
            tmp.enableWordWrapping = false;
            tmp.alignment          = TextAlignmentOptions.MidlineLeft;
            tmp.margin             = new Vector4(6, 0, 6, 0);
        }

        private static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
        }

        // ── Status rows ───────────────────────────────────────────────────────

        internal void RefreshRows()
        {
            try
            {
                if (ContentParent == null) { TrackerLog.LogError("[LST] ContentParent null"); return; }
                EnsureHeaderRows();

                Company player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
                ResourceDefinition supplyRD = player != null
                    ? AllScriptableObjectManager.Instance?.AllResourceDefinitions?.GetByID("id_resource_supply")
                    : null;
                var allObjects = supplyRD != null
                    ? MonoBehaviourSingleton<ObjectInfoManager>.Instance?.allObjectInfos
                    : null;

                if (player == null || supplyRD == null || allObjects == null)
                {
                    bool structChanged = SyncColonyRows(new List<ColonyData>()) | SyncVehicleRows(new List<VehicleData>());
                    bool wasEmpty = _emptyMsgGO.activeSelf;
                    _emptyMsgLbl.text = player == null ? "Not in game yet." : "Loading game data...";
                    if (!wasEmpty) { _emptyMsgGO.SetActive(true); structChanged = true; }
                    if (_shipSepGO.activeSelf) { _shipSepGO.SetActive(false); structChanged = true; }
                    if (_shipLblGO.activeSelf) { _shipLblGO.SetActive(false); structChanged = true; }
                    _titleLbl.text = "LIFE SUPPORT STATUS";
                    if (structChanged) LayoutRebuilder.ForceRebuildLayoutImmediate(ContentParent as RectTransform);
                    return;
                }

                var colonies = new List<ColonyData>();
                foreach (ObjectInfo oi in allObjects)
                {
                    if (oi.IsInGameDestroy) continue; // skip crashed/destroyed bodies — game keeps them in allObjectInfos with crew intact
                    ObjectInfoData data = oi.GetObjectInfoData(player);
                    if (data == null || data.CurrentCrew <= 0) continue;

                    double supply  = data.CheckResources(supplyRD);
                    double perDay  = data.GetSupplyDemandPerDay();
                    double days    = perDay > 0.0 ? supply / perDay : double.PositiveInfinity;
                    var (pop, habCap) = data.GetPopulationHabitats();
                    RowResourcesData supplyRow = data.ListRowResourcesData
                        .FirstOrDefault(r => r.ResourcesType?.ID == "id_resource_supply");
                    double prodPerDay   = supplyRow?.InTake ?? 0.0;
                    double incomePerDay = ComputeColonistIncomePerDay(pop, habCap, oi);

                    colonies.Add(new ColonyData { OI = oi, Days = days, Supply = supply, PerDay = perDay, ProdPerDay = prodPerDay, Pop = pop, IncomePerDay = incomePerDay });
                }
                colonies.Sort((a, b) => a.Days.CompareTo(b.Days));
                _lastActiveBodyNames = colonies.Select(c => c.OI.ObjectName).ToList();

                var vehicles = new List<VehicleData>();
                float lsMult = MonoBehaviourSingleton<GameManager>.Instance?.Economic
                    .GetLifeSupportMultiplayer(player) ?? 5f;
                var allShips = MonoBehaviourSingleton<ShipManager>.Instance?.ListAllSpaceShip;
                if (allShips != null)
                {
                    foreach (Spacecraft sc in allShips)
                    {
                        if (!sc.GetCompany().IsPlayer) continue;
                        if (sc.CurrentPhase != Spacecraft.EPhase.Fly    &&
                            sc.CurrentPhase != Spacecraft.EPhase.Launch  &&
                            sc.CurrentPhase != Spacecraft.EPhase.Landing) continue;
                        int crew = sc.CargoAll.HowMuchCrew();
                        if (crew <= 0) continue;
                        int scheduledLS = sc.GetLifeSupportCurrentWhenFly();
                        double cargoLS   = sc.CargoAll.GetLifeSupportFromCargoSupply();
                        double totalLS   = Math.Max(0, scheduledLS) + cargoLS;
                        double consPerDayLS = crew / (double)lsMult;
                        double days = consPerDayLS > 0 && totalLS > 0
                            ? totalLS / consPerDayLS
                            : double.PositiveInfinity;
                        ObjectInfo originOI = null;
                        try { originOI = sc.MissionStart; } catch { }
                        string shipSprite = ResolveShipSprite(sc, TrackerLog);

                        double daysToArrival = double.PositiveInfinity;
                        try
                        {
                            MissionInfo mi = sc.GetMissionInfo();
                            TimeController tc = MonoBehaviourSingleton<TimeController>.Instance;
                            if (mi != null && tc != null)
                            {
                                double eta = (mi.DateArrive - tc.CurrentTime).TotalDays;
                                daysToArrival = eta > 0 ? eta : 0;
                            }
                        }
                        catch (Exception etaEx) { TrackerLog.LogWarning($"[LST] ETA: {etaEx.Message}"); }

                        vehicles.Add(new VehicleData {
                            SC               = sc,
                            Name             = sc.GetSpacecraftName(),
                            Destination      = sc.MissionTarget?.ObjectName ?? "?",
                            Crew             = crew,
                            CurrentLS        = totalLS,
                            ConsPerDayLS     = consPerDayLS,
                            Days             = days,
                            DaysToArrival    = daysToArrival,
                            OriginName       = originOI?.ObjectName ?? "",
                            OriginSpriteName = originOI?.ImagePlanetUI?.name ?? "",
                            DestSpriteName   = sc.MissionTarget?.ImagePlanetUI?.name ?? "",
                            ShipSpriteName   = shipSprite,
                        });
                    }
                    vehicles.Sort((a, b) => a.Days.CompareTo(b.Days));
                }

                int bodyCount = colonies.Count;
                int shipCount = vehicles.Count;
                _titleLbl.text = $"LIFE SUPPORT STATUS  ({bodyCount} {(bodyCount == 1 ? "body" : "bodies")}, {shipCount} {(shipCount == 1 ? "ship" : "ships")})";

                bool changed = SyncColonyRows(colonies);
                changed |= SyncVehicleRows(vehicles);

                bool empty = colonies.Count == 0 && vehicles.Count == 0;
                if (_emptyMsgGO.activeSelf != empty) { _emptyMsgGO.SetActive(empty); changed = true; }
                if (empty) _emptyMsgLbl.text = "No colonized bodies or ships with crew found.";

                bool hasShips = vehicles.Count > 0;
                if (_shipSepGO.activeSelf != hasShips) { _shipSepGO.SetActive(hasShips); changed = true; }
                if (_shipLblGO.activeSelf != hasShips) { _shipLblGO.SetActive(hasShips); changed = true; }

                ReorderContent(colonies, vehicles);
                if (changed) LayoutRebuilder.ForceRebuildLayoutImmediate(ContentParent as RectTransform);

                Severity overall = Severity.OK;
                foreach (var c in colonies)
                {
                    var (warn, crit) = Config.GetThresholds(c.OI.ObjectName);
                    Severity s = c.Days <= crit ? Severity.Critical : c.Days <= warn ? Severity.Warning : Severity.OK;
                    if (s > overall) overall = s;
                }
                foreach (var v in vehicles)
                {
                    bool willArrive = v.DaysToArrival <= 0 || v.Days >= v.DaysToArrival;
                    if (!willArrive)
                    {
                        Severity s = (v.Days < v.DaysToArrival - 7) ? Severity.Critical : Severity.Warning;
                        if (s > overall) overall = s;
                    }
                }
                UpdateIndicatorSeverity(overall);
            }
            catch (Exception e)
            {
                TrackerLog.LogError($"[LST] RefreshRows exception: {e}");
            }
        }

        private void EnsureHeaderRows()
        {
            if (_headerBuilt) return;
            _headerBuilt = true;

            _titleRowGO = new GameObject("TitleRow", typeof(RectTransform));
            _titleRowGO.transform.SetParent(ContentParent, false);
            _titleRowGO.AddComponent<LayoutElement>().preferredHeight = 26f;
            _titleLbl = _titleRowGO.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) _titleLbl.font = FontAsset;
            _titleLbl.fontSize           = 12f;
            _titleLbl.fontStyle          = FontStyles.Bold;
            _titleLbl.color              = Color.white;
            _titleLbl.enableWordWrapping = false;
            _titleLbl.alignment          = TextAlignmentOptions.MidlineLeft;
            _titleLbl.margin             = new Vector4(6, 4, 6, 0);

            (_headerRowGO, _headerSepGO) = AddHeaderRow();

            _emptyMsgGO = new GameObject("MsgRow", typeof(RectTransform));
            _emptyMsgGO.transform.SetParent(ContentParent, false);
            _emptyMsgGO.AddComponent<LayoutElement>().preferredHeight = 20f;
            _emptyMsgLbl = _emptyMsgGO.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) _emptyMsgLbl.font = FontAsset;
            _emptyMsgLbl.fontSize           = 11f;
            _emptyMsgLbl.color              = new Color(0.65f, 0.65f, 0.65f);
            _emptyMsgLbl.enableWordWrapping = false;
            _emptyMsgLbl.alignment          = TextAlignmentOptions.MidlineLeft;
            _emptyMsgLbl.margin             = new Vector4(6, 0, 6, 0);
            _emptyMsgGO.SetActive(false);

            _shipSepGO = new GameObject("SectionSep", typeof(RectTransform));
            _shipSepGO.transform.SetParent(ContentParent, false);
            _shipSepGO.AddComponent<LayoutElement>().preferredHeight = 1f;
            _shipSepGO.AddComponent<Image>().color = new Color(0.35f, 0.35f, 0.35f, 1f);
            _shipSepGO.SetActive(false);

            _shipLblGO = new GameObject("SectionLabel", typeof(RectTransform));
            _shipLblGO.transform.SetParent(ContentParent, false);
            _shipLblGO.AddComponent<LayoutElement>().preferredHeight = 18f;
            var shipLbl = _shipLblGO.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) shipLbl.font = FontAsset;
            shipLbl.text               = "SHIPS IN TRANSIT";
            shipLbl.fontSize           = 10f;
            shipLbl.fontStyle          = FontStyles.Bold;
            shipLbl.color              = new Color(0.55f, 0.55f, 0.55f);
            shipLbl.enableWordWrapping = false;
            shipLbl.alignment          = TextAlignmentOptions.MidlineLeft;
            shipLbl.margin             = new Vector4(6, 2, 6, 0);
            _shipLblGO.SetActive(false);
        }

        private bool SyncColonyRows(List<ColonyData> colonies)
        {
            bool changed = false;
            var activeNames = new HashSet<string>(colonies.Select(c => c.OI.ObjectName));

            var stale = new List<string>();
            foreach (var key in _colonyRowCache.Keys)
                if (!activeNames.Contains(key)) stale.Add(key);
            foreach (var key in stale)
            {
                UnityEngine.Object.DestroyImmediate(_colonyRowCache[key].GO);
                _colonyRowCache.Remove(key);
                changed = true;
            }

            foreach (var c in colonies)
            {
                var (warn, crit) = Config.GetThresholds(c.OI.ObjectName);
                if (!_colonyRowCache.TryGetValue(c.OI.ObjectName, out var cache))
                {
                    _colonyRowCache[c.OI.ObjectName] = CreateColonyRow(c, warn, crit);
                    changed = true;
                }
                else
                {
                    UpdateColonyRow(cache, c, warn, crit);
                }
            }
            return changed;
        }

        private bool SyncVehicleRows(List<VehicleData> vehicles)
        {
            bool changed = false;
            var activeNames = new HashSet<string>(vehicles.Select(v => v.Name));

            var stale = new List<string>();
            foreach (var key in _vehicleRowCache.Keys)
                if (!activeNames.Contains(key)) stale.Add(key);
            foreach (var key in stale)
            {
                UnityEngine.Object.DestroyImmediate(_vehicleRowCache[key].GO);
                _vehicleRowCache.Remove(key);
                changed = true;
            }

            foreach (var v in vehicles)
            {
                if (!_vehicleRowCache.TryGetValue(v.Name, out var cache))
                {
                    _vehicleRowCache[v.Name] = CreateVehicleRow(v);
                    changed = true;
                }
                else
                {
                    UpdateVehicleRow(cache, v);
                }
            }
            return changed;
        }

        private void ReorderContent(List<ColonyData> colonies, List<VehicleData> vehicles)
        {
            int idx = 0;
            _titleRowGO.transform.SetSiblingIndex(idx++);
            _headerRowGO.transform.SetSiblingIndex(idx++);
            _headerSepGO.transform.SetSiblingIndex(idx++);
            foreach (var c in colonies)
                if (_colonyRowCache.TryGetValue(c.OI.ObjectName, out var row))
                    row.GO.transform.SetSiblingIndex(idx++);
            _emptyMsgGO.transform.SetSiblingIndex(idx++);
            _shipSepGO.transform.SetSiblingIndex(idx++);
            _shipLblGO.transform.SetSiblingIndex(idx++);
            foreach (var v in vehicles)
                if (_vehicleRowCache.TryGetValue(v.Name, out var row))
                    row.GO.transform.SetSiblingIndex(idx++);
        }

        private StatusRowCache CreateColonyRow(ColonyData c, double warn, double crit)
        {
            GameObject rowGO = MakeRowContainer($"Row_{c.OI.ObjectName}", 22f);
            rowGO.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            var cache = new StatusRowCache
            {
                GO        = rowGO,
                NameCol   = AddColumn(rowGO.transform, 0f, 1f, TextAlignmentOptions.MidlineLeft, "", 120f),
                PopCol    = AddColumn(rowGO.transform, ColPop,    0f, TextAlignmentOptions.MidlineRight, ""),
                SupplyCol = AddColumn(rowGO.transform, ColSupply, 0f, TextAlignmentOptions.MidlineRight, ""),
                ProdCol   = AddColumn(rowGO.transform, ColProd,   0f, TextAlignmentOptions.MidlineRight, ""),
                ConsCol   = AddColumn(rowGO.transform, ColCons,   0f, TextAlignmentOptions.MidlineRight, ""),
                IncomeCol = AddColumn(rowGO.transform, ColIncome, 0f, TextAlignmentOptions.MidlineRight, ""),
                DaysCol   = AddColumn(rowGO.transform, ColDays,   0f, TextAlignmentOptions.MidlineRight, ""),
                Btn       = rowGO.AddComponent<Button>(),
            };
            UpdateColonyRow(cache, c, warn, crit);
            return cache;
        }

        private void UpdateColonyRow(StatusRowCache cache, ColonyData c, double warn, double crit)
        {
            if (!ReferenceEquals(cache.BoundIdentity, c.OI))
            {
                cache.Btn.onClick.RemoveAllListeners();
                ObjectInfo oiRef = c.OI;
                cache.Btn.onClick.AddListener(() =>
                {
                    try { UIManager.Instance.Open(EWindowType.ObjectInfo, oiRef); }
                    catch (Exception e) { TrackerLog.LogError($"[LST] colony click: {e.Message}"); }
                });
                cache.BoundIdentity = c.OI;
            }

            string dotColorHex = c.Days <= crit ? "#FF3333" : c.Days <= warn ? "#FF9900" : "#44BB44";
            string spriteName  = c.OI.ImagePlanetUI?.name ?? "";
            string icon        = spriteName.Length > 0 ? $"<sprite name={spriteName}> " : "";
            cache.NameCol.text  = $"<color={dotColorHex}>●</color>  {icon}{c.OI.ObjectName}";
            cache.NameCol.color = Color.white;
            cache.PopCol.text   = $"{c.Pop}";
            cache.PopCol.color  = new Color(0.85f, 0.85f, 0.85f);
            cache.SupplyCol.text  = FormatSupply(c.Supply);
            cache.SupplyCol.color = new Color(0.85f, 0.85f, 0.85f);
            cache.ProdCol.text    = FormatConsumption(c.ProdPerDay);
            cache.ProdCol.color   = new Color(0.85f, 0.85f, 0.85f);
            cache.ConsCol.text    = FormatConsumption(c.PerDay);
            cache.ConsCol.color   = new Color(0.85f, 0.85f, 0.85f);
            Color daysColor = c.Days <= crit ? new Color(1f, 0.2f, 0.2f)
                : c.Days <= warn ? new Color(1f, 0.6f, 0f)
                : new Color(0.85f, 0.85f, 0.85f);
            cache.DaysCol.text  = FormatDays(c.Days);
            cache.DaysCol.color = daysColor;
            cache.IncomeCol.text  = FormatIncome(c.IncomePerDay);
            cache.IncomeCol.color = new Color(0.85f, 0.85f, 0.85f);
        }

        private StatusRowCache CreateVehicleRow(VehicleData v)
        {
            GameObject rowGO = MakeRowContainer($"VRow_{v.Name}", 22f);
            rowGO.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            var cache = new StatusRowCache
            {
                GO        = rowGO,
                NameCol   = AddColumn(rowGO.transform, 0f, 1f, TextAlignmentOptions.MidlineLeft, "", 120f),
                PopCol    = AddColumn(rowGO.transform, ColPop,    0f, TextAlignmentOptions.MidlineRight, ""),
                SupplyCol = AddColumn(rowGO.transform, ColSupply, 0f, TextAlignmentOptions.MidlineRight, ""),
                ProdCol   = AddColumn(rowGO.transform, ColProd,   0f, TextAlignmentOptions.MidlineRight, ""),
                ConsCol   = AddColumn(rowGO.transform, ColCons,   0f, TextAlignmentOptions.MidlineRight, ""),
                IncomeCol = AddColumn(rowGO.transform, ColIncome, 0f, TextAlignmentOptions.MidlineRight, ""),
                DaysCol   = AddColumn(rowGO.transform, ColDays,   0f, TextAlignmentOptions.MidlineRight, ""),
                Btn       = rowGO.AddComponent<Button>(),
            };
            UpdateVehicleRow(cache, v);
            return cache;
        }

        private void UpdateVehicleRow(StatusRowCache cache, VehicleData v)
        {
            if (!ReferenceEquals(cache.BoundIdentity, v.SC))
            {
                cache.Btn.onClick.RemoveAllListeners();
                Spacecraft scRef = v.SC;
                cache.Btn.onClick.AddListener(() =>
                {
                    try { UIManager.Instance.Open(EWindowType.SpaceCraftInfo, scRef); }
                    catch (Exception e) { TrackerLog.LogError($"[LST] ship click: {e.Message}"); }
                });
                cache.BoundIdentity = v.SC;
            }

            var (defWarn, defCrit) = Config.Defaults;
            bool willArrive = v.DaysToArrival <= 0 || v.Days >= v.DaysToArrival;

            string dotColorHex;
            if (double.IsPositiveInfinity(v.DaysToArrival))
                dotColorHex = v.Days <= defCrit ? "#FF3333" : v.Days <= defWarn ? "#FF9900" : "#44BB44";
            else
                dotColorHex = willArrive ? "#44BB44" : v.Days >= v.DaysToArrival - 7 ? "#FF9900" : "#FF3333";

            string originIcon = v.OriginSpriteName.Length > 0 ? $"<sprite name={v.OriginSpriteName}> " : "";
            string originPart = v.OriginName.Length > 0 ? $"{originIcon}{v.OriginName}" : v.Name;
            string shipIcon   = v.ShipSpriteName.Length > 0 ? $"<sprite name={v.ShipSpriteName}>" : "▶";
            string destPart   = v.DestSpriteName.Length > 0
                ? $"<sprite name={v.DestSpriteName}> {v.Destination}"
                : v.Destination;
            cache.NameCol.text  = $"<color={dotColorHex}>●</color>  {originPart} <color=#888888>→ {shipIcon} → {destPart}</color>";
            cache.NameCol.color = Color.white;
            cache.PopCol.text   = $"{v.Crew}";
            cache.PopCol.color  = new Color(0.85f, 0.85f, 0.85f);
            cache.SupplyCol.text  = FormatSupply(v.CurrentLS / 365.0);
            cache.SupplyCol.color = new Color(0.85f, 0.85f, 0.85f);
            cache.ProdCol.text    = $"{U}—{UE}";
            cache.ProdCol.color   = new Color(0.85f, 0.85f, 0.85f);
            cache.ConsCol.text    = FormatConsumption(v.ConsPerDayLS / 365.0);
            cache.ConsCol.color   = new Color(0.85f, 0.85f, 0.85f);

            Color daysColor;
            if (double.IsPositiveInfinity(v.DaysToArrival))
                daysColor = v.Days <= defCrit ? new Color(1f, 0.2f, 0.2f)
                    : v.Days <= defWarn ? new Color(1f, 0.6f, 0f)
                    : new Color(0.85f, 0.85f, 0.85f);
            else
                daysColor = willArrive ? new Color(0.85f, 0.85f, 0.85f)
                    : v.Days >= v.DaysToArrival - 7 ? new Color(1f, 0.6f, 0f)
                    : new Color(1f, 0.2f, 0.2f);

            string etaSuffix = double.IsPositiveInfinity(v.DaysToArrival) ? ""
                : $"<color=#555555> /{v.DaysToArrival:F0}d</color>";
            cache.DaysCol.text  = FormatDays(v.Days) + etaSuffix;
            cache.DaysCol.color = daysColor;
            cache.IncomeCol.text  = $"{U}—{UE}";
            cache.IncomeCol.color = new Color(0.85f, 0.85f, 0.85f);
        }

        private void AddTitleRow(string text, Transform parent)
        {
            GameObject go = new GameObject("TitleRow", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = 26f;
            TextMeshProUGUI lbl = go.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) lbl.font = FontAsset;
            lbl.text               = text;
            lbl.fontSize           = 12f;
            lbl.fontStyle          = FontStyles.Bold;
            lbl.color              = Color.white;
            lbl.enableWordWrapping = false;
            lbl.alignment          = TextAlignmentOptions.MidlineLeft;
            lbl.margin             = new Vector4(6, 4, 6, 0);
        }

        private (GameObject row, GameObject sep) AddHeaderRow()
        {
            GameObject rowGO = MakeRowContainer("HeaderRow", 20f);
            Color hc = new Color(0.55f, 0.55f, 0.55f);
            AddColumn(rowGO.transform, 0f, 1f, TextAlignmentOptions.MidlineLeft,  "BODY",     120f).color = hc;
            AddColumn(rowGO.transform, ColPop,    0f, TextAlignmentOptions.MidlineRight, "POP").color     = hc;
            AddColumn(rowGO.transform, ColSupply, 0f, TextAlignmentOptions.MidlineRight, "SUPPLY").color  = hc;
            AddColumn(rowGO.transform, ColProd,   0f, TextAlignmentOptions.MidlineRight, "PROD/DAY").color = hc;
            AddColumn(rowGO.transform, ColCons,   0f, TextAlignmentOptions.MidlineRight, "CONS/DAY").color = hc;
            AddColumn(rowGO.transform, ColIncome, 0f, TextAlignmentOptions.MidlineRight, _supportsColonistIncome ? "INCOME" : "").color = hc;
            AddColumn(rowGO.transform, ColDays,   0f, TextAlignmentOptions.MidlineRight, "LEFT").color    = hc;

            GameObject sep = new GameObject("Separator", typeof(RectTransform));
            sep.transform.SetParent(ContentParent, false);
            sep.AddComponent<LayoutElement>().preferredHeight = 1f;
            sep.AddComponent<Image>().color = new Color(0.35f, 0.35f, 0.35f, 1f);
            return (rowGO, sep);
        }


        private GameObject MakeRowContainer(string name, float height)
        {
            GameObject rowGO = new GameObject(name, typeof(RectTransform));
            rowGO.transform.SetParent(ContentParent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = height;
            HorizontalLayoutGroup hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth      = true;
            hlg.childForceExpandWidth  = false;
            hlg.childControlHeight     = true;
            hlg.childForceExpandHeight = true;
            hlg.spacing = 4f;
            hlg.padding = new RectOffset(6, 6, 0, 0);
            return rowGO;
        }

        private TextMeshProUGUI AddColumn(Transform parent, float preferredWidth, float flexibleWidth,
            TextAlignmentOptions align, string text, float minWidth = -1f)
        {
            GameObject go = new GameObject("Col", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            if (minWidth >= 0f) le.minWidth = minWidth;
            le.preferredWidth = preferredWidth;
            le.flexibleWidth  = flexibleWidth;
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) tmp.font = FontAsset;
            tmp.text               = text;
            tmp.fontSize           = 11f;
            tmp.enableWordWrapping = false;
            tmp.overflowMode       = TextOverflowModes.Ellipsis;
            tmp.alignment          = align;
            tmp.raycastTarget      = false;
            return tmp;
        }

        private const string U  = "<color=#888888>";
        private const string UE = "</color>";

        private static string FormatSupply(double supply)
        {
            if (supply >= 1_000_000) return $"{supply / 1_000_000:F1}{U}MT{UE}";
            if (supply >= 1_000)     return $"{supply / 1_000:F1}{U}KT{UE}";
            return $"{supply:F0}{U}T{UE}";
        }

        private static string FormatConsumption(double rate)
        {
            if (rate <= 0)   return $"0{U}T/d{UE}";
            if (rate < 0.01) return $"{rate:F3}{U}T/d{UE}";
            if (rate < 1)    return $"{rate:F2}{U}T/d{UE}";
            return $"{rate:F1}{U}T/d{UE}";
        }

        private static string FormatDays(double days)
        {
            if (double.IsPositiveInfinity(days)) return "∞";
            if (days >= 365.25) return $"{days / 365.25:F1}{U}y{UE}";
            return $"{days:F0}{U}d{UE}";
        }

        private static double ComputeColonistIncomePerDay(long crew, long habCap, ObjectInfo oi)
        {
            if (!_supportsColonistIncome) return double.NaN;
            try
            {
                float baseRate = (float)_propHumanDailyMoneyProduction.GetValue(MonoBehaviourSingleton<GameManager>.Instance.Economic);
                float mult     = (float)_propHumanDailyMoneyProductionMultiplier.GetValue(oi);
                return (double)Math.Min(crew, habCap) * mult * baseRate;
            }
            catch { return double.NaN; }
        }

        private static string FormatIncome(double income)
        {
            if (double.IsNaN(income)) return $"{U}—{UE}";
            if (income <= 0)          return $"0{U}$/d{UE}";
            if (income >= 1_000_000)  return $"{income / 1_000_000:F1}{U}M$/d{UE}";
            if (income >= 1_000)      return $"{income / 1_000:F1}{U}k$/d{UE}";
            return $"{income:F0}{U}$/d{UE}";
        }

        private void UpdateIndicatorSeverity(Severity severity)
        {
            if (IndicatorLabel == null) return;
            bool critical = severity == Severity.Critical;
            string dotHex = severity == Severity.Critical ? "#FF3333"
                : severity == Severity.Warning ? "#FF9900"
                : "#44BB44";
            IndicatorLabel.text = $"<color={dotHex}>●</color>  LIFE SUPPORT";
            if (Mover != null) Mover.IsCritical = critical;
        }


        private static string ResolveShipSprite(Spacecraft sc, ManualLogSource log)
        {
            try
            {
                string id = sc.spacecraftType?.SpriteId;
                if (!string.IsNullOrEmpty(id)) return id;
            }
            catch (Exception e) { log?.LogWarning($"[LST] ResolveShipSprite: {e.Message}"); }
            return "";
        }


        private struct ColonyData
        {
            public ObjectInfo OI;
            public double Days, Supply, PerDay, ProdPerDay, IncomePerDay;
            public long Pop;
        }

        private struct VehicleData
        {
            public Spacecraft SC;
            public string Name, Destination;
            public int Crew;
            public double CurrentLS, ConsPerDayLS, Days, DaysToArrival;
            public string OriginName, OriginSpriteName, DestSpriteName, ShipSpriteName;
        }
    }

    // ── Resize handle ─────────────────────────────────────────────────────────
    internal class ResizeHandle : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler,
        IDragHandler
    {
        internal RectTransform PanelRT;
        private const float MinHeight = 200f;

        private static Texture2D _cursor;
        private Canvas _canvas;
        private bool _dragging;
        private Vector2 _dragStartScreen;
        private float _dragStartHeight;

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            if (_cursor == null) _cursor = BuildCursor();
        }

        public void OnPointerEnter(PointerEventData e) =>
            Cursor.SetCursor(_cursor, new Vector2(16, 16), CursorMode.Auto);

        public void OnPointerExit(PointerEventData e)
        {
            if (!_dragging) Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        public void OnPointerDown(PointerEventData e)
        {
            _dragging        = true;
            _dragStartScreen = e.position;
            _dragStartHeight = PanelRT.sizeDelta.y;
        }

        public void OnPointerUp(PointerEventData e)
        {
            _dragging = false;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        public void OnDrag(PointerEventData e)
        {
            float scale  = _canvas != null ? _canvas.scaleFactor : 1f;
            float delta  = (e.position.y - _dragStartScreen.y) / scale;
            float height = Mathf.Max(MinHeight, _dragStartHeight - delta);
            PanelRT.sizeDelta = new Vector2(PanelRT.sizeDelta.x, height);
        }

        private static Texture2D BuildCursor()
        {
            const int S  = 32;
            const int cx = 15;
            Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            Color[] px = new Color[S * S];
            for (int i = 0; i < px.Length; i++) px[i] = Color.clear;

            void Dot(int x, int y, Color c) { if (x >= 0 && x < S && y >= 0 && y < S) px[y * S + x] = c; }
            void Line(int x, bool outline) { Color core = Color.white; Color ol = Color.black; for (int y = 9; y < S - 9; y++) Dot(x, y, outline ? ol : core); }

            Line(cx - 1, true); Line(cx + 1, true); Line(cx, false);

            for (int i = 0; i < 6; i++)
            {
                int y = S - 3 - i;
                for (int x = cx - i; x <= cx + i; x++) Dot(x, y, Color.white);
                Dot(cx - i - 1, y, Color.black);
                Dot(cx + i + 1, y, Color.black);
            }
            for (int x = cx - 1; x <= cx + 1; x++) Dot(x, S - 2, Color.black);

            for (int i = 0; i < 6; i++)
            {
                int y = 2 + i;
                for (int x = cx - i; x <= cx + i; x++) Dot(x, y, Color.white);
                Dot(cx - i - 1, y, Color.black);
                Dot(cx + i + 1, y, Color.black);
            }
            for (int x = cx - 1; x <= cx + 1; x++) Dot(x, 1, Color.black);

            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }

    // ── Per-body alert threshold config ───────────────────────────────────────
    internal class SupplyTrackerConfig
    {
        private const double DefaultWarn = 180.0; // 6 months
        private const double DefaultCrit = 90.0;  // 3 months

        private readonly ConfigEntry<string> _entry;
        private (double warn, double crit) _defaults;
        private readonly Dictionary<string, (double warn, double crit)> _perBody;

        internal SupplyTrackerConfig(ConfigFile cfg)
        {
            _entry   = cfg.Bind("LifeSupport", "Thresholds", "",
                "Per-body warning/critical thresholds in days. Managed by the in-game settings tab.");
            _defaults = (DefaultWarn, DefaultCrit);
            _perBody  = new Dictionary<string, (double, double)>();
            Load();
        }

        internal (double warn, double crit) Defaults => _defaults;

        internal (double warn, double crit) GetThresholds(string bodyName)
            => _perBody.TryGetValue(bodyName, out var v) ? v : _defaults;

        internal void SetDefaults(double warn, double crit)
        {
            _defaults = (Math.Max(1, warn), Math.Max(1, crit));
            Save();
        }

        internal void SetBody(string bodyName, double warn, double crit)
        {
            _perBody[bodyName] = (Math.Max(1, warn), Math.Max(1, crit));
            Save();
        }

        internal void PruneToActive(IEnumerable<string> activeNames)
        {
            var active = new HashSet<string>(activeNames);
            var stale  = _perBody.Keys.Where(k => !active.Contains(k)).ToList();
            if (stale.Count == 0) return;
            foreach (var k in stale) _perBody.Remove(k);
            Save();
        }

        // Format: "__DEFAULT__=180,90;Mars=90,30;..."
        // Keys are backslash-escaped to handle edge-case names.
        private void Save()
        {
            var sb = new StringBuilder();
            sb.Append($"__DEFAULT__={_defaults.warn:F0},{_defaults.crit:F0}");
            foreach (var kv in _perBody)
                sb.Append($";{Esc(kv.Key)}={kv.Value.warn:F0},{kv.Value.crit:F0}");
            _entry.Value = sb.ToString();
        }

        private void Load()
        {
            string raw = _entry.Value;
            if (string.IsNullOrEmpty(raw)) return;
            foreach (var part in raw.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq < 0) continue;
                string key = Unesc(part.Substring(0, eq));
                string val = part.Substring(eq + 1);
                int comma  = val.IndexOf(',');
                if (comma < 0) continue;
                if (!double.TryParse(val.Substring(0, comma),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double w)) continue;
                if (!double.TryParse(val.Substring(comma + 1),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double c)) continue;
                if (key == "__DEFAULT__") _defaults = (w, c);
                else _perBody[key] = (w, c);
            }
        }

        private static string Esc(string s)   => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace("=", "\\=");
        private static string Unesc(string s) => s.Replace("\\=", "=").Replace("\\;", ";").Replace("\\\\", "\\");
    }

    // Persistent updater — lives on the always-active indicator button so it keeps
    // ticking and updating the severity dot even when the panel is closed.
    internal class TrackerUpdater : MonoBehaviour
    {
        internal SupplyTrackerPanel Tracker;
        private float _timer;

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= SupplyTrackerPanel.RefreshInterval)
            {
                _timer = 0f;
                Tracker?.RefreshRows();
            }
        }

        private void LateUpdate() => Patches.PauseScreenEscPatch.LateUpdateTick();
    }

}
