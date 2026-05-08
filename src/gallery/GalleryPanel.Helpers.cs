using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace VPB
{
    public class UIHoverDelegate : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action<bool> OnHoverChange;
        public Action<PointerEventData> OnPointerEnterEvent;
        private bool isHovered = false;

        public void OnPointerEnter(PointerEventData d) 
        {
            if (isHovered) return;
            isHovered = true;
            OnHoverChange?.Invoke(true);
            OnPointerEnterEvent?.Invoke(d);
        }

        public void OnPointerExit(PointerEventData d) 
        {
            if (!isHovered) return;
            isHovered = false;
            OnHoverChange?.Invoke(false);
        }

        private void OnDisable()
        {
            if (isHovered)
            {
                isHovered = false;
                OnHoverChange?.Invoke(false);
            }
        }
    }

    public class UIRightClickDelegate : MonoBehaviour, IPointerClickHandler
    {
        public Action OnRightClick;
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                OnRightClick?.Invoke();
            }
        }
    }

    /// <summary>
    /// Left selection on <see cref="IPointerUpHandler"/> with tap slop: ScrollRect only steals <b>left</b> drags,
    /// so <see cref="Button.onClick"/> drops taps that moved past the drag threshold; right-click uses
    /// <see cref="IPointerClickHandler"/> and does not fight the scroll view the same way.
    /// </summary>
    /// <summary>
    /// Thumbnail <see cref="RawImage"/> sits above row root and steals raycasts. Forward <see cref="IPointerUpHandler"/>
    /// to root <see cref="UIFileEntryLeftReleaseSelect"/> so <see cref="UIDraggableItem"/> / hold-to-launch / slop logic
    /// runs on correct GameObject (duplicate <c>UIFileEntryLeftReleaseSelect</c> on thumb used <c>GetComponent</c> on wrong transform).
    /// </summary>
    internal sealed class GalleryThumbPointerForwarder : MonoBehaviour, IPointerUpHandler
    {
        public UIFileEntryLeftReleaseSelect Target;

        public void OnPointerUp(PointerEventData eventData)
        {
            if (Target != null) Target.OnPointerUp(eventData);
        }
    }

    public sealed class UIFileEntryLeftReleaseSelect : MonoBehaviour, IPointerUpHandler
    {
        public GalleryPanel Panel;
        public FileEntry File;
        private const float TapSlopPixels = 22f;

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left) return;
            if (Panel == null || File == null) return;
            var dragItem = GetComponent<UIDraggableItem>();
            if (dragItem != null && dragItem.IsLongPress) return;
            if (Panel.HoldToLaunchEnabled && dragItem != null && dragItem.LastPointerDownUnscaledTime >= 0f)
            {
                float holdSec = 1f;
                try
                {
                    if (VPBConfig.Instance != null)
                        holdSec = Mathf.Clamp(VPBConfig.Instance.HoldToLaunchHoldSeconds, 0.2f, 1f);
                }
                catch { }
                if (Time.unscaledTime - dragItem.LastPointerDownUnscaledTime >= holdSec - 0.001f)
                    return;
            }
            Vector2 delta = (Vector2)eventData.position - eventData.pressPosition;
            if (delta.sqrMagnitude > TapSlopPixels * TapSlopPixels) return;
            Panel.OnFileClick(File);
        }
    }

    public class RatingHandler : MonoBehaviour
    {
        private FileEntry entry;
        private string uid;
        private Text starIconText;
        private Image starIconImage;
        private GameObject selectorGO;
        private CanvasGroup selectorCG;
        private int currentRating = 0;
        private Image[] optionImages;
        private Text[] optionTexts;
        private GameObject[] borderGOs;

        public static readonly Color[] RatingColors = new Color[]
        {
            new Color(1f, 1f, 1f, 0.2f),     // 0: Ghost White (unrated)
            new Color(1f, 0.2f, 0.2f, 1f),   // 1: Red
            new Color(1f, 0.55f, 0f, 1f),    // 2: Orange
            new Color(1f, 0.85f, 0f, 1f),    // 3: Gold
            new Color(0.2f, 0.85f, 0.2f, 1f),// 4: Green
            new Color(0f, 0.9f, 1f, 1f)      // 5: Cyan
        };

        public void Init(FileEntry e, Text s, GameObject selector)
        {
            bool sameUid = (uid == e?.Uid);
            entry = e;
            uid = e?.Uid;
            starIconText = s;
            selectorGO = selector;
            if (selectorGO != null)
            {
                selectorCG = selectorGO.GetComponent<CanvasGroup>();
                if (selectorCG == null) selectorCG = selectorGO.AddComponent<CanvasGroup>();
                // Do not auto-close selector during refresh; refresh can rebind rows and swap FileEntry instances,
                // which would otherwise close the popup immediately after opening (notably in Custom Scenes).
            }
            
            try { currentRating = RatingsManager.Instance.GetRating(e); }
            catch { currentRating = 0; }
            UpdateDisplay();
        }

        public void Init(string id, Text s, GameObject selector)
        {
            bool sameUid = (uid == id);
            entry = null;
            uid = id;
            starIconText = s;
            selectorGO = selector;
            if (selectorGO != null)
            {
                selectorCG = selectorGO.GetComponent<CanvasGroup>();
                if (selectorCG == null) selectorCG = selectorGO.AddComponent<CanvasGroup>();
                // Do not auto-close selector during refresh; refresh can rebind ids while user is interacting.
            }

            try { currentRating = RatingsManager.Instance.GetRating(uid); }
            catch { currentRating = 0; }
            UpdateDisplay();
        }

        private void SetSelectorVisible(bool visible)
        {
            if (selectorGO == null) return;
            if (selectorCG == null) selectorCG = selectorGO.GetComponent<CanvasGroup>();
            if (selectorCG == null) selectorCG = selectorGO.AddComponent<CanvasGroup>();
            selectorCG.alpha = visible ? 1f : 0f;
            selectorCG.interactable = visible;
            selectorCG.blocksRaycasts = visible;
        }

        public void ToggleSelector()
        {
            if (selectorGO == null) return;
            if (selectorCG == null) selectorCG = selectorGO.GetComponent<CanvasGroup>();
            bool nextState = selectorCG == null || selectorCG.alpha <= 0.01f;
            SetSelectorVisible(nextState);
        }

        public void CloseSelector()
        {
            SetSelectorVisible(false);
        }

        public void SetRating(int rating)
        {
            currentRating = rating;
            if (entry != null) RatingsManager.Instance.SetRating(entry, rating);
            else RatingsManager.Instance.SetRating(uid, rating);
            UpdateDisplay();
            SetSelectorVisible(false);
        }

        public void SetOptionRefs(Image[] images, Text[] texts, GameObject[] borders)
        {
            optionImages = images;
            optionTexts = texts;
            borderGOs = borders;
            UpdateDisplay();
        }

        public void BindStarIcon(Image iconImage)
        {
            starIconImage = iconImage;
            UpdateDisplay();
        }

        public void SetDisplayOnly(int rating)
        {
            currentRating = Mathf.Clamp(rating, 0, 5);
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            Color c = RatingColors[Mathf.Clamp(currentRating, 0, 5)];
            if (starIconText != null)
                starIconText.color = c;
            if (starIconImage != null)
                starIconImage.color = c;

            if (optionImages == null) return;
            for (int i = 0; i < optionImages.Length && i < 6; i++)
            {
                bool selected = (i == currentRating);
                if (optionImages[i] != null)
                    optionImages[i].color = RatingColors[i];
                if (optionTexts != null && i < optionTexts.Length && optionTexts[i] != null)
                    optionTexts[i].color = i == 0 ? Color.red : Color.black;
                if (borderGOs != null && i < borderGOs.Length && borderGOs[i] != null)
                    borderGOs[i].SetActive(selected);
            }
        }
    }

    public class SearchInputESCHandler : MonoBehaviour
    {
        private InputField inputField;
        private Button clearButton;
        private bool refocusQueued;

        public void Initialize(InputField input, Button clearBtn = null)
        {
            inputField = input;
            clearButton = clearBtn;
        }

        private void OnGUI()
        {
            if (inputField == null || !inputField.isFocused) return;
            Event e = Event.current;
            if (e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                e.Use();
                if (clearButton != null) clearButton.onClick?.Invoke();
                else
                {
                    inputField.text = "";
                    inputField.ActivateInputField();
                    inputField.MoveTextEnd(false);
                }
                if (!refocusQueued && inputField != null)
                {
                    refocusQueued = true;
                    StartCoroutine(Refocus());
                }
            }
        }

        private IEnumerator Refocus()
        {
            yield return null;
            refocusQueued = false;
            if (inputField != null)
            {
                inputField.ActivateInputField();
                inputField.MoveTextEnd(false);
            }
        }
    }

    /// <summary>
    /// Adds standard Ctrl+Backspace (delete previous word) behavior to Unity <see cref="InputField"/>.
    /// Unity's built-in InputField handling often lacks typical editor shortcuts.
    /// </summary>
    public class CtrlBackspaceWordDeleteHandler : MonoBehaviour
    {
        private InputField inputField;

        public void Initialize(InputField input)
        {
            inputField = input;
        }

        private void OnGUI()
        {
            if (inputField == null || !inputField.isFocused) return;
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return;

            // Ctrl+Backspace (Windows/Linux) / Cmd+Backspace (macOS): delete previous word
            bool accel = e.control || e.command;
            if (!accel || e.keyCode != KeyCode.Backspace) return;

            string text = inputField.text ?? "";
            if (text.Length == 0)
            {
                e.Use();
                return;
            }

            // If there's an active selection, delete it.
            int a = inputField.selectionAnchorPosition;
            int b = inputField.selectionFocusPosition;
            if (a != b)
            {
                int start = Mathf.Clamp(Math.Min(a, b), 0, text.Length);
                int end = Mathf.Clamp(Math.Max(a, b), 0, text.Length);
                string newText = text.Remove(start, end - start);
                inputField.text = newText;
                inputField.caretPosition = start;
                inputField.selectionAnchorPosition = start;
                inputField.selectionFocusPosition = start;
                e.Use();
                return;
            }

            int caret = Mathf.Clamp(inputField.caretPosition, 0, text.Length);
            if (caret == 0)
            {
                e.Use();
                return;
            }

            int i = caret;
            // First delete any whitespace directly behind the caret (so repeated Ctrl+Backspace behaves naturally).
            while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--;
            // Then delete the previous "word" chunk.
            while (i > 0 && !char.IsWhiteSpace(text[i - 1])) i--;

            if (i < caret)
            {
                string newText = text.Remove(i, caret - i);
                inputField.text = newText;
                inputField.caretPosition = i;
                inputField.selectionAnchorPosition = i;
                inputField.selectionFocusPosition = i;
            }

            e.Use();
        }
    }

    public class UIScrollWheelHandler : MonoBehaviour, IScrollHandler
    {
        public Action<float> OnScrollValue;
        public float Sensitivity = 0.1f;

        public void OnScroll(PointerEventData eventData)
        {
            if (Mathf.Abs(eventData.scrollDelta.y) > 0.01f)
            {
                OnScrollValue?.Invoke(eventData.scrollDelta.y * Sensitivity);
            }
        }
    }
}
