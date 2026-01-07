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

    public class FavoriteHandler : MonoBehaviour
    {
        private FileEntry entry;
        private Text iconText;
        private bool isHovering = false;
        private bool isFavorite = false;

        public void Init(FileEntry e, Text t)
        {
            entry = e;
            iconText = t;
            try
            {
                if (FavoritesManager.Instance != null)
                {
                    isFavorite = FavoritesManager.Instance.IsFavorite(entry);
                    FavoritesManager.Instance.OnFavoriteChanged += OnFavChanged;
                }
            }
            catch (Exception) { }
            UpdateState();
        }

        public void SetHover(bool hover)
        {
            isHovering = hover;
            UpdateState();
        }

        private void OnFavChanged(string uid, bool fav)
        {
            if (entry != null && entry.Uid == uid)
            {
                isFavorite = fav;
                UpdateState();
            }
        }

        private void UpdateState()
        {
            // Logic: Visible if Favorite OR Hovering
            bool shouldShow = isFavorite || isHovering;
            
            gameObject.SetActive(shouldShow);

            if (iconText != null)
            {
                // Color: Yellow if Favorite, otherwise White with alpha (Ghost)
                iconText.color = isFavorite ? Color.yellow : new Color(1f, 1f, 1f, 0.5f);
            }
        }

        void OnDestroy()
        {
            try
            {
                // Only unsubscribe if we successfully subscribed (which means Instance worked)
                FavoritesManager.Instance.OnFavoriteChanged -= OnFavChanged;
            }
            catch { }
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
