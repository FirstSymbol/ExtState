using System;
using UnityEngine;

namespace ExtState
{
  public enum StateMachineLogLevel
  {
    None = 0,
    Error = 1,
    Warning = 2,
    Info = 3
  }

  /// <summary>
  /// Minimal logger of the state machines. Assign <see cref="Handler"/> to route messages to any logging backend.
  /// </summary>
  public static class StateMachineLog
  {
    public const string Tag = "ExtState";

    public static StateMachineLogLevel Level { get; set; } = StateMachineLogLevel.Warning;

    /// <summary>Custom output. When null, messages go to <see cref="Debug.unityLogger"/>.</summary>
    public static Action<LogType, string> Handler { get; set; }

    public static bool IsEnabled(StateMachineLogLevel level) => level != StateMachineLogLevel.None && Level >= level;

    public static void Info(string message) => Write(StateMachineLogLevel.Info, LogType.Log, message);
    public static void Warning(string message) => Write(StateMachineLogLevel.Warning, LogType.Warning, message);
    public static void Error(string message) => Write(StateMachineLogLevel.Error, LogType.Error, message);

    private static void Write(StateMachineLogLevel level, LogType type, string message)
    {
      if (!IsEnabled(level))
        return;

      if (Handler != null)
        Handler(type, message);
      else
        Debug.unityLogger.Log(type, Tag, message);
    }
  }
}
