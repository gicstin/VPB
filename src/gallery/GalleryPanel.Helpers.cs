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
                // Temporarily disable right click in desktop mode
                if (!UnityEngine.XR.XRSettings.enabled) return;

                OnRightClick?.Invoke();
            }
        }
    }

    public class RatingHandler : MonoBehaviour
    {
        private FileEntry entry;
        private string uid;
        private Text starIconText;
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
                if (!sameUid) SetSelectorVisible(false);
            }
            
            currentRating = RatingsManager.Instance.GetRating(e);
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
                if (!sameUid) SetSelectorVisible(false);
            }

            currentRating = RatingsManager.Instance.GetRating(uid);
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

        private void UpdateDisplay()
        {
            if (starIconText != null)
                starIconText.color = RatingColors[Mathf.Clamp(currentRating, 0, 5)];

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
