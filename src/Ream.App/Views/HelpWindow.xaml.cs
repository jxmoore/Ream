using System.Windows;
using Ream.Core.Models;

namespace Ream.App.Views;

/// <summary>Read-only list of every shortcut, as currently configured.</summary>
public partial class HelpWindow : Window
{
    public HelpWindow(IReadOnlyList<HelpSection> sections)
    {
        InitializeComponent();
        Sections.ItemsSource = sections;
    }
}
