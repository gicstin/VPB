using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Unity multiline <see cref="InputField"/> treats Home/End as whole buffer. Caveman want current line only (Notepad / VSCode style).
    /// Runs LateUpdate after default InputField so caveman fix caret when Home/End pressed this frame.
    /// </summary>
    [RequireComponent(typeof(InputField))]
    internal sealed class MultilineInputFieldHomeEndFix : MonoBehaviour
    {
        private InputField _field;
        private int _caretAtEndOfLastFrame;
        private bool _hadFocus;

        private void Awake()
        {
            _field = GetComponent<InputField>();
        }

        private void OnEnable()
        {
            _caretAtEndOfLastFrame = 0;
            _hadFocus = false;
        }

        private void LateUpdate()
        {
            if (_field == null || !_field.isFocused)
            {
                _hadFocus = false;
                return;
            }
            if (!_hadFocus)
            {
                _hadFocus = true;
                _caretAtEndOfLastFrame = _field.caretPosition;
            }
            if (_field.lineType == InputField.LineType.SingleLine)
            {
                _caretAtEndOfLastFrame = _field.caretPosition;
                return;
            }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (!ctrl)
            {
                if (Input.GetKeyDown(KeyCode.Home))
                    ApplyLineHomeOrEnd(isHome: true);
                if (Input.GetKeyDown(KeyCode.End))
                    ApplyLineHomeOrEnd(isHome: false);
            }

            _caretAtEndOfLastFrame = _field.caretPosition;
        }

        private void ApplyLineHomeOrEnd(bool isHome)
        {
            string t = _field.text ?? "";
            int remembered = _caretAtEndOfLastFrame;
            if (remembered < 0) remembered = 0;
            if (remembered > t.Length) remembered = t.Length;

            int lineStart = FindCurrentLineStart(t, remembered);
            int lineEnd = FindCurrentLineEndExclusive(t, remembered);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (isHome)
            {
                if (!shift)
                {
                    _field.selectionAnchorPosition = lineStart;
                    _field.caretPosition = lineStart;
                }
                else
                {
                    _field.selectionAnchorPosition = remembered;
                    _field.caretPosition = lineStart;
                }
            }
            else
            {
                if (!shift)
                {
                    _field.selectionAnchorPosition = lineEnd;
                    _field.caretPosition = lineEnd;
                }
                else
                {
                    _field.selectionAnchorPosition = remembered;
                    _field.caretPosition = lineEnd;
                }
            }
        }

        private static int FindCurrentLineStart(string t, int caretBefore)
        {
            if (t.Length == 0) return 0;
            int i = caretBefore;
            if (i > t.Length) i = t.Length;
            while (i > 0)
            {
                char c = t[i - 1];
                if (c == '\n')
                    break;
                i--;
            }
            return i;
        }

        private static int FindCurrentLineEndExclusive(string t, int caretBefore)
        {
            if (t.Length == 0) return 0;
            int i = caretBefore;
            if (i < 0) i = 0;
            if (i > t.Length) i = t.Length;
            while (i < t.Length && t[i] != '\n')
                i++;
            return i;
        }
    }
}
