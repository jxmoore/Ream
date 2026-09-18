using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Ream.Core.Models;

namespace Ream.App.Input;

internal static class KeyBindingsRegistry
{
    /// <summary>
    /// Binds each action's gesture string (e.g. "Alt+Shift+Left") to its command. Config is hand-edited,
    /// so a gesture that is invalid or already taken by another action falls back to that action's
    /// default; if the default is unusable too the action is left unbound. Never throws.
    /// </summary>
    public static void Apply(
        Window window,
        IReadOnlyDictionary<string, string> gestures,
        IReadOnlyDictionary<string, ICommand> commands)
    {
        var defaults = AppConfig.DefaultKeybindings();
        var converter = new KeyGestureConverter();
        var taken = new HashSet<(Key, ModifierKeys)>();

        foreach (var (action, text) in gestures)
        {
            if (!commands.TryGetValue(action, out var command)) continue;

            var gesture = TryClaim(converter, text, taken);
            if (gesture is null && defaults.TryGetValue(action, out var fallback))
            {
                Debug.WriteLine($"Keybinding for '{action}' ('{text}') is invalid or in use; using default '{fallback}'.");
                gesture = TryClaim(converter, fallback, taken);
            }

            if (gesture is null)
            {
                Debug.WriteLine($"No usable keybinding for '{action}'; leaving it unbound.");
                continue;
            }

            window.InputBindings.Add(new KeyBinding(command, gesture));
        }
    }

    private static KeyGesture? TryClaim(KeyGestureConverter converter, string text, HashSet<(Key, ModifierKeys)> taken)
    {
        try
        {
            if (converter.ConvertFromString(text) is not KeyGesture gesture) return null;
            return taken.Add((gesture.Key, gesture.Modifiers)) ? gesture : null;
        }
        catch (Exception ex) when (ex is NotSupportedException or FormatException or ArgumentException)
        {
            return null;
        }
    }
}
