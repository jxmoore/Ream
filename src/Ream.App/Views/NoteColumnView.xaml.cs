using System.Windows.Controls;
using System.Windows.Input;
using Ream.App.ViewModels;

namespace Ream.App.Views;

public partial class NoteColumnView : UserControl
{
    public NoteColumnView()
    {
        InitializeComponent();
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is NoteViewModel note)
            note.FocusCommand.Execute(null);
    }
}
