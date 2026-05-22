using Avalonia.Controls;
using GlossaAI.Desktop.ViewModels;
using System;

namespace GlossaAI.Desktop.Views;

/// <summary>
/// Interaction logic for MainWindow.axaml.
/// </summary>
public partial class MainWindow : Window
{
    private bool _isRealClose;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Event triggered when the window opens. Triggers audio device loading.
    /// </summary>
    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        
        if (DataContext is MainViewModel vm)
        {
            await vm.LoadDevicesCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Overrides standard close action to hide the window to the System Tray instead of exiting.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_isRealClose)
        {
            e.Cancel = true;
            this.Hide();
        }
        base.OnClosing(e);
    }

    /// <summary>
    /// Programmatically closes the application completely (bypassing the hide behavior).
    /// </summary>
    public void RealClose()
    {
        _isRealClose = true;
        this.Close();
    }

    /// <summary>
    /// Reset MainWindow reference on desktop lifetime once closed.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow == this)
        {
            desktop.MainWindow = null;
        }
    }
}