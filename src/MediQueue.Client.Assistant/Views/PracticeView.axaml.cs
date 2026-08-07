using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MediQueue.Client.Assistant.Views;

/// <summary>The assistant's screen. Everything it does is in the view model.</summary>
public partial class PracticeView : UserControl
{
    /// <summary>Creates the view.</summary>
    public PracticeView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
