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
        public void OnPointerEnter(PointerEventData d) 
        {
            OnHoverChange?.Invoke(true);
            OnPointerEnterEvent?.Invoke(d);
        }
        public void OnPointerExit(PointerEventData d) => OnHoverChange?.Invoke(false);
    }

    public class UIRightClickDelegate : MonoBehaviour, IPointerClickHandler
    {
        public Action OnRightClick;
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
                OnRightClick?.Invoke();
        }
    }

    public class RatingHandler : MonoBehaviour
    {
        private FileEntry entry;
        private Text starIconText;
        private GameObject selectorGO;
        private CanvasGroup selectorCG;
        private int currentRating = 0;

        public static readonly Color[] RatingColors = new Color[]
        {
            new Color(1f, 1f, 1f, 0.2f),     // 0: Ghost White
            new Color(0.7f, 0.7f, 0.7f, 1f), // 1: Silver/Gray
            new Color(0.2f, 0.8f, 0.2f, 1f), // 2: Green
            new Color(1f, 0.85f, 0f, 1f),   // 3: Gold
            new Color(1f, 0.5f, 0f, 1f),     // 4: Orange
            new Color(1f, 0.2f, 0.8f, 1f)    // 5: Magenta/Pink
        };

        public void Init(FileEntry e, Text s, GameObject selector)
        {
            entry = e;
            starIconText = s;
            selectorGO = selector;
            if (selectorGO != null)
            {
                selectorCG = selectorGO.GetComponent<CanvasGroup>();
                if (selectorCG == null) selectorCG = selectorGO.AddComponent<CanvasGroup>();
                SetSelectorVisible(false);
            }
            
            currentRating = RatingsManager.Instance.GetRating(entry);
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
            RatingsManager.Instance.SetRating(entry, rating);
            UpdateDisplay();
            SetSelectorVisible(false);
        }

        private void UpdateDisplay()
        {
            if (starIconText != null)
            {
                starIconText.color = RatingColors[Mathf.Clamp(currentRating, 0, 5)];
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
