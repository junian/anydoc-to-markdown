using Avalonia.Controls;
using AvaloniaUIDemo.ViewModels;

namespace AvaloniaUIDemo.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnWindowOpened(object? sender, System.EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Attach(this);
        }
    }
}