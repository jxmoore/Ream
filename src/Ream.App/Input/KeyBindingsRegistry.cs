using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace Ream.App.Input;

internal static class KeyBindingsRegistry
{
    /// <summary>
    /// Binds each action's gesture string (e.g. "Alt+Shift+Left") to its command.
    /// Unknown actions and unparseable gestures are skipped, never fatal.
    /// </summary>
    public static void Apply(
        Window window,
        IReadOnlyDictionary<string, string> gestures,
        IReadOnlyDictionary<string, ICommand> commands)
    {
        var converter = new KeyGestureConverter();

        foreach (var (action, text) in gestures)
        {
            if (!commands.TryGetValue(action, out var command)) continue;

            try
            {
                if (converter.ConvertFromString(text) is KeyGesture gesture)
                    window.InputBindings.Add(new KeyBinding(command, gesture));
            }
            catch (Exception ex) when (ex is NotSupportedException or FormatException)
            {
                Debug.WriteLine($"Ignoring invalid keybinding for '{action}': '{text}' ({ex.Message})");
            }
        }
    }
}
