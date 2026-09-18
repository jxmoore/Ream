using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ream.Core.Models;

namespace Ream.App.ViewModels;

public sealed partial class NoteViewModel : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _body = "";

    [ObservableProperty]
    private string _accentColor = "#7c9cff";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WidthFraction), nameof(WidthLabel))]
    private WidthPreset _widthPreset = WidthPreset.Half;

    [ObservableProperty]
    private bool _isFullscreen;

    [ObservableProperty]
    private bool _isFocused;

    public double WidthFraction => WidthPreset.Fraction();

    public string WidthLabel => WidthPreset.Label();

    internal WorkspaceViewModel? Owner { get; set; }

    [RelayCommand]
    private void Focus() => Owner?.SetFocus(Owner.Notes.IndexOf(this));
}
