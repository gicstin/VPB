using BepInEx.Configuration;
using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.IO;

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
        private static readonly VpbLogRepeatGovernor Repeats = new VpbLogRepeatGovernor();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly string SessionName = VpbSessionLog.NewSessionName();
        private static volatile VpbSessionLog _session;
        private static double _nextPoll;
        private static double _nextStatistics;
        public static volatile bool Verbose;

        internal static void Dispatch(VPBLogEventArgs args)
        {
            // Fatal and explicit verbose output must never be hidden by repeat suppression.
            if (!Verbose && (args.Level & LogLevel.Fatal) == 0)
            {
                var key = new VpbLogRepeatGovernor.Key
                {
                    Source = args.DisplaySourceName, Message = args.Data == null ? "" : args.Data.ToString(),
                    Level = (int)args.Level, ShowInGame = args.ShowInGame
                };
                long summary;
                bool emit = Repeats.Accept(key, Clock.Elapsed.TotalSeconds, out summary);
                if (summary != 0) EmitRepeatSummary(key, summary);
                if (!emit) return;
            }
            args.Source.Emit(args);
        }

        private static void EmitRepeatSummary(VpbLogRepeatGovernor.Key key, long count)
        {
            string text = count < 0 ? "REPEAT suppressing further copies for 30s: "
                : "REPEAT suppressed=" + count + ": ";
            Main.Emit(new VPBLogEventArgs(text + key.Message, (LogLevel)key.Level, Main, false, key.Source));
        }

        internal static void Poll()
        {
            double now = Clock.Elapsed.TotalSeconds;
            if (now < _nextPoll) return;
            _nextPoll = now + 1;
            RefreshOptions();
            if (now >= _nextStatistics)
            {
                _nextStatistics = now + 30;
                LogUtil.LogPerfSummary("periodic");
            }
            foreach (var entry in Repeats.Flush(now, false))
                EmitRepeatSummary(entry.Key, entry.Count - VpbLogRepeatGovernor.Copies);
            var session = _session;
            string error = session == null ? null : session.TakeError();
            if (error != null) Main.LogWarning(error, false);
        }

        internal static void RefreshOptions()
        {
            var entry = Settings.Instance.VerboseLogging;
            Verbose = entry != null && entry.Value;
        }

        internal static void Flush()
        {
            foreach (var entry in Repeats.Flush(Clock.Elapsed.TotalSeconds, true))
                EmitRepeatSummary(entry.Key, entry.Count - VpbLogRepeatGovernor.Copies);
            var session = _session;
            if (session != null) session.Flush();
        }

        internal static void WriteSession(VPBLogEventArgs args)
        {
            var session = _session;
            if (session != null)
                session.Write(args.ToString(), (args.Level & (LogLevel.Fatal | LogLevel.Error)) != 0);
        }

        public static void Init()
        {
            if (_IsInit) return;
            _IsInit = true;
            _session = new VpbSessionLog(Path.Combine(BepInEx.Paths.BepInExRootPath, "VPB/logs"), SessionName);
            foreach (var logger in _instances.Values)
                if (!Logger.Sources.Contains(logger)) Logger.Sources.Add(logger);
            Main.LogInfo("Setting up VPB loggers");
            Logger.Listeners.Add(_inGameLogger);
        }

        public static void Destroy()
        {
            _IsInit = false;
            Main.LogInfo("Cleaning up VPB loggers");
            LogUtil.LogPerfSummary("teardown");
            Flush();
            var session = _session;
            if (session != null) session.Dispose();
            _session = null;
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
            VPBLogger.Dispatch(args);
        }

        internal void Emit(VPBLogEventArgs args)
        {
            VPBLogger.WriteSession(args);
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
        internal string DisplaySourceName { get; }
        private readonly DateTime timestamp = DateTime.UtcNow;
        public VPBLogEventArgs(object data, LogLevel level, VPBLogSource source, bool showInGame, string displaySourceName = null) : base(data, level, source)
        {
            Source = source;
            ShowInGame = showInGame;
            // OneShot source names change between calls; retain this event's identity.
            DisplaySourceName = displaySourceName ?? source.OverrideSourceName ?? source.SourceName;
        }

        public override string ToString()
        {
            return $"[{Level,-7}:{DisplaySourceName,10}] {timestamp.ToString("o")} {Data}";
        }
    }
}
