using System.Collections.Generic;
using UnityEngine;

namespace var_browser
{
	public class KeyUtil
	{
		public List<KeyCode> supportKeys = new List<KeyCode>();

		public KeyCode key;
		public string keyPattern;

		public static KeyUtil Parse(string keyPattern)
		{
			string[] array = keyPattern.Split('+');
			List<KeyCode> list = new List<KeyCode>();
			//string text;
			KeyCode code=KeyCode.Home;
			if (array.Length == 1)
			{
				string text = array[0];
				code = ParseKeyCode(text);
			}
			else
			{
				for (int i = 0; i < array.Length - 1; i++)
				{
					string a = array[i].ToLower();
					if (a == "ctrl")
					{
						list.Add(KeyCode.LeftControl);
						list.Add(KeyCode.RightControl);
					}
					else if (a == "shift")
					{
						list.Add(KeyCode.LeftShift);
						list.Add(KeyCode.RightShift);
					}
					else if (a == "alt")
					{
						list.Add(KeyCode.LeftAlt);
						list.Add(KeyCode.RightAlt);
					}
                    else
                    {
						list.Add(ParseKeyCode(array[i]));
					}
				}
				string text = array[array.Length - 1];
				code = ParseKeyCode(text);
			}
			return new KeyUtil
			{
				supportKeys = list,
				key = code,
				keyPattern = keyPattern
		};
		}

		private static KeyCode ParseKeyCode(string val)
		{
			if (string.IsNullOrEmpty(val)) return KeyCode.None;

			switch (val)
			{
				case "`": return KeyCode.BackQuote;
				case "~": return KeyCode.BackQuote;
				case "§": return KeyCode.BackQuote; // Add support for Section sign
				case "0": return KeyCode.Alpha0;
				case "1": return KeyCode.Alpha1;
				case "2": return KeyCode.Alpha2;
				case "3": return KeyCode.Alpha3;
				case "4": return KeyCode.Alpha4;
				case "5": return KeyCode.Alpha5;
				case "6": return KeyCode.Alpha6;
				case "7": return KeyCode.Alpha7;
				case "8": return KeyCode.Alpha8;
				case "9": return KeyCode.Alpha9;
				case "-": return KeyCode.Minus;
				case "=": return KeyCode.Equals;
				case "[": return KeyCode.LeftBracket;
				case "]": return KeyCode.RightBracket;
				case "\\": return KeyCode.Backslash;
				case ";": return KeyCode.Semicolon;
				case "'": return KeyCode.Quote;
				case ",": return KeyCode.Comma;
				case ".": return KeyCode.Period;
				case "/": return KeyCode.Slash;
			}
			return (KeyCode)System.Enum.Parse(typeof(KeyCode), val, true);
		}

		public bool TestKeyUp()
		{
			if (Input.GetKeyUp(key))
			{
				return TestSupports();
			}
			return false;
		}

		public bool TestKeyDown()
		{
			if (Input.GetKeyDown(key))
			{
				return TestSupports();
			}
			return false;
		}

		public bool TestSupports()
		{
			for (int i = 0; i < supportKeys.Count; i += 2)
			{
				if (!Input.GetKey(supportKeys[i]) && !Input.GetKey(supportKeys[i + 1]))
				{
					return false;
				}
			}
			return true;
		}

		public bool IsSame(KeyUtil other)
		{
			if (other == null) return false;
			if (this.key == KeyCode.None || other.key == KeyCode.None) return false;
			if (this.key != other.key) return false;
			if (this.supportKeys.Count != other.supportKeys.Count) return false;

			for (int i = 0; i < this.supportKeys.Count; i += 2)
			{
				KeyCode k1 = this.supportKeys[i];
				KeyCode k2 = this.supportKeys[i+1];
				bool found = false;
				for (int j = 0; j < other.supportKeys.Count; j += 2)
				{
					if (other.supportKeys[j] == k1 && other.supportKeys[j+1] == k2)
					{
						found = true;
						break;
					}
				}
				if (!found) return false;
			}
			return true;
		}
	}
}
