using UnityEngine;

namespace VPB
{
    public static class AnchorPresets
    {
        public const int none = -1;
        public const int topLeft = 0;
        public const int topMiddle = 1;
        public const int topRight = 2;
        public const int vStretchLeft = 3;
        public const int vStretchMiddle = 4;
        public const int vStretchRight = 5;
        public const int bottomLeft = 6;
        public const int bottomMiddle = 7;
        public const int bottomRight = 8;
        public const int hStretchTop = 9;
        public const int hStretchMiddle = 10;
        public const int hStretchBottom = 11;
        public const int centre = 12;
        public const int stretchAll = 13;
        public const int middleLeft = 14;
        public const int middleRight = 15;
        public const int middleCenter = 16;

        public static Vector2 GetAnchorMin(int preset)
        {
            switch (preset)
            {
                case topLeft: return new Vector2(0, 1);
                case topMiddle: return new Vector2(0.5f, 1);
                case topRight: return new Vector2(1, 1);
                case vStretchLeft: return new Vector2(0, 0);
                case vStretchMiddle: return new Vector2(0.5f, 0);
                case vStretchRight: return new Vector2(1, 0);
                case bottomLeft: return new Vector2(0, 0);
                case bottomMiddle: return new Vector2(0.5f, 0);
                case bottomRight: return new Vector2(1, 0);
                case hStretchTop: return new Vector2(0, 1);
                case hStretchMiddle: return new Vector2(0, 0.5f);
                case hStretchBottom: return new Vector2(0, 0);
                case centre: return new Vector2(0.5f, 0.5f);
                case stretchAll: return new Vector2(0, 0);
                case middleLeft: return new Vector2(0, 0.5f);
                case middleRight: return new Vector2(1, 0.5f);
                case middleCenter: return new Vector2(0.5f, 0.5f);
                default: return Vector2.zero;
            }
        }

        public static Vector2 GetAnchorMax(int preset)
        {
            switch (preset)
            {
                case topLeft: return new Vector2(0, 1);
                case topMiddle: return new Vector2(0.5f, 1);
                case topRight: return new Vector2(1, 1);
                case vStretchLeft: return new Vector2(0, 1);
                case vStretchMiddle: return new Vector2(0.5f, 1);
                case vStretchRight: return new Vector2(1, 1);
                case bottomLeft: return new Vector2(0, 0);
                case bottomMiddle: return new Vector2(0.5f, 0);
                case bottomRight: return new Vector2(1, 0);
                case hStretchTop: return new Vector2(1, 1);
                case hStretchMiddle: return new Vector2(1, 0.5f);
                case hStretchBottom: return new Vector2(1, 0);
                case centre: return new Vector2(0.5f, 0.5f);
                case stretchAll: return new Vector2(1, 1);
                case middleLeft: return new Vector2(0, 0.5f);
                case middleRight: return new Vector2(1, 0.5f);
                case middleCenter: return new Vector2(0.5f, 0.5f);
                default: return Vector2.zero;
            }
        }

        public static Vector2 GetPivot(int preset)
        {
            switch (preset)
            {
                case topLeft: return new Vector2(0, 1);
                case topMiddle: return new Vector2(0.5f, 1);
                case topRight: return new Vector2(1, 1);
                case vStretchLeft: return new Vector2(0, 0.5f);
                case vStretchMiddle: return new Vector2(0.5f, 0.5f);
                case vStretchRight: return new Vector2(1, 0.5f);
                case bottomLeft: return new Vector2(0, 0);
                case bottomMiddle: return new Vector2(0.5f, 0);
                case bottomRight: return new Vector2(1, 0);
                case hStretchTop: return new Vector2(0.5f, 1);
                case hStretchMiddle: return new Vector2(0.5f, 0.5f);
                case hStretchBottom: return new Vector2(0.5f, 0);
                case centre: return new Vector2(0.5f, 0.5f);
                case stretchAll: return new Vector2(0.5f, 0.5f);
                case middleLeft: return new Vector2(0, 0.5f);
                case middleRight: return new Vector2(1, 0.5f);
                case middleCenter: return new Vector2(0.5f, 0.5f);
                default: return new Vector2(0.5f, 0.5f);
            }
        }
    }
}
