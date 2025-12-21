using System;
using System.Diagnostics;
using UnityEngine;

namespace var_browser
{
    class LogUtil
    {
        static readonly DateTime processStartTime;
        static readonly Stopwatch sincePluginAwake = new Stopwatch();
        static bool pluginAwakeMarked;
        static bool readyLogged;
        static double? readyProcessSeconds;

        static LogUtil()
        {
            try
            {
                processStartTime = Process.GetCurrentProcess().StartTime;
            }
            catch
            {
                processStartTime = DateTime.Now;
            }
        }

        public static void MarkPluginAwake()
        {
            if (pluginAwakeMarked)
            {
                return;
            }

            pluginAwakeMarked = true;
            sincePluginAwake.Start();
        }

        public static void Log(string log)
        {
            UnityEngine.Debug.Log(DateTime.Now.ToString("HH:mm:ss")+"【var browser】" + log);
        }
        public static void LogError(string log)
        {
            UnityEngine.Debug.LogError(DateTime.Now.ToString("HH:mm:ss") + "【var browser】" + log);
        }
        public static void LogWarning(string log)
        {
            UnityEngine.Debug.LogWarning(DateTime.Now.ToString("HH:mm:ss") + "【var browser】" + log);
        }

        public static void LogReadyOnce(string context)
        {
            if (readyLogged)
            {
                return;
            }

            readyLogged = true;

            var sinceProcessStart = DateTime.Now - processStartTime;
            readyProcessSeconds = sinceProcessStart.TotalSeconds;
            var sincePluginStart = sincePluginAwake.IsRunning ? sincePluginAwake.Elapsed : TimeSpan.Zero;
            LogWarning(string.Format("READY {0} | since process start: {1:0.000}s | since plugin awake: {2:0.000}s", context, sinceProcessStart.TotalSeconds, sincePluginStart.TotalSeconds));
        }

        public static double GetSecondsSinceProcessStart()
        {
            return (DateTime.Now - processStartTime).TotalSeconds;
        }

        public static double GetStartupSecondsForDisplay()
        {
            if (readyProcessSeconds.HasValue)
            {
                return readyProcessSeconds.Value;
            }

            return GetSecondsSinceProcessStart();
        }
    }
}
