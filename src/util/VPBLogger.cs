using BepInEx.Configuration;
using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VPB.src.util
{
    public enum VPBModule
    {
        Main = 0,
        Config,
        Files,
        Gallery,
        Hub,
        Zstd,
        Hooks,
        Perf,
        Custom,
    }

    public class VPBLogger
    {
        public static readonly LogLevel GlobalLogLevels = LogLevel.Fatal | LogLevel.Error | LogLevel.Warning | LogLevel.Message | LogLevel.Info;

        private static Dictionary<VPBModule, VPBLogSource> _instances = new Dictionary<VPBModule, VPBLogSource>();


        public static VPBLogSource Main = GetInstance(VPBModule.Main);
        public static VPBLogSource Config = GetInstance(VPBModule.Config);
        public static VPBLogSource Files = GetInstance(VPBModule.Files);
        public static VPBLogSource Gallery = GetInstance(VPBModule.Gallery);
        public static VPBLogSource Hub = GetInstance(VPBModule.Hub);
        public static VPBLogSource Zstd = GetInstance(VPBModule.Zstd);
        public static VPBLogSource Hooks = GetInstance(VPBModule.Hooks);
        public static VPBLogSource Perf = GetInstance(VPBModule.Perf);
        private static VPBLogSource _oneShotInstance = GetInstance(VPBModule.Custom);

        public static VPBLogSource GetInstance(VPBModule module, bool defaultShowInGame = true)
        {
            if (!_instances.TryGetValue(module, out VPBLogSource instance))
            {
                instance = new VPBLogSource(module, defaultShowInGame);
                Logger.Sources.Add(instance);
                _instances[module] = instance;
            }
            return instance;
        }

        public static VPBLogSource OneShot(string name, bool showInGame = true)
        {
            _oneShotInstance.OverrideSourceName = name;
            return _oneShotInstance;
        }

        private static VPBInGameLogListener _inGameLogger = new VPBInGameLogListener();

        private static bool _IsInit = false;

        public static void Init()
        {
            if (_IsInit) return;
            _IsInit = true;
            Main.LogInfo("Setting up VPB loggers");
            Logger.Listeners.Add(_inGameLogger);
        }

        public static void Destroy()
        {
            _IsInit = false;
            Main.LogInfo("Cleaning up VPB loggers");
            Logger.Listeners.Remove(_inGameLogger);
            if (_oneShotInstance != null) Logger.Sources.Remove(_oneShotInstance);
            foreach (var logger in _instances.Values)
            {
                Logger.Sources.Remove(logger);
            }
        }
    }

    public class VPBLogSource : ILogSource, IDisposable
    {

        public string SourceName { get; }

        public string OverrideSourceName = null;

        public bool ShowInGame;

        public event EventHandler<LogEventArgs> LogEvent;

        public LogLevel OverrideLogLevels = LogLevel.None;

        public VPBLogSource(VPBModule module, bool showInGame = true)
        {
            SourceName = module == VPBModule.Main ? "VPB" : ("VPB." + module.ToString());
            ShowInGame = showInGame;
        }

        public VPBLogSource(string name, bool showInGame = true)
        {
            SourceName = name;
            ShowInGame = showInGame;
        }

        public void Log(LogLevel level, object data, bool showInGame)
        {
            if ((level & (VPBLogger.GlobalLogLevels | OverrideLogLevels)) == LogLevel.None) return;
            var args = new VPBLogEventArgs(data, level, this, showInGame);
            this.LogEvent?.Invoke(this, args);
        }

        /// <summary>
        /// Log a message of critical importance
        /// </summary>
        public void LogFatal(object data, bool showInGame = true)
        {
            Log(LogLevel.Fatal, data, showInGame);
        }

        /// <summary>
        /// Log a message of high importance
        /// </summary>
        public void LogError(object data, bool showInGame = true)
        {
            Log(LogLevel.Error, data, showInGame);
        }

        /// <summary>
        /// Log a message of some importance
        /// </summary>
        public void LogWarning(object data, bool showInGame = true)
        {
            Log(LogLevel.Warning, data, showInGame);
        }

        /// <summary>
        /// Log a message of minimal importance
        /// </summary>
        public void LogMessage(object data, bool showInGame = true)
        {
            Log(LogLevel.Message, data, showInGame);
        }

        /// <summary>
        /// Log a message of no importance
        /// </summary>
        public void LogInfo(object data, bool showInGame = false)
        {
            Log(LogLevel.Info, data, showInGame);
        }

        /// <summary>
        /// Log a message for debugging
        /// </summary>
        public void LogDebug(object data, bool showInGame = false)
        {
            Log(LogLevel.Debug, data, showInGame);
        }

        public void Dispose()
        {
        }
    }

    public class VPBInGameLogListener : ILogListener, IDisposable
    {
        public static LogLevel LogToErrorLog = LogLevel.Fatal | LogLevel.Error;
        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            if (eventArgs is VPBLogEventArgs vpbEventArgs && vpbEventArgs.ShowInGame && SuperController.singleton != null)
            {
                if ((vpbEventArgs.Level & LogToErrorLog) != LogLevel.None)
                {
                    SuperController.singleton.Error(vpbEventArgs.ToString(), logToFile: false, splash: true);
                }
                else
                {
                    SuperController.singleton.Message(vpbEventArgs.ToString(), logToFile: false, splash: true);
                }
            }
        }

        public void Dispose()
        {
        }
    }

    public class VPBLogEventArgs : LogEventArgs
    {
        public new VPBLogSource Source { get; protected set; }
        public bool ShowInGame { get; }
        public VPBLogEventArgs(object data, LogLevel level, VPBLogSource source, bool showInGame) : base(data, level, source)
        {
            Source = source;
            ShowInGame = showInGame;
        }

        public override string ToString()
        {
            return $"[{Level,-7}:{Source.OverrideSourceName ?? Source.SourceName,10}] {DateTime.Now.ToString("HH:mm:ss")} {Data}";
        }
    }
}
