using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GlossaAI.Core.Domain.Interfaces;
using GlossaAI.Core.Domain.Models;
using GlossaAI.Core.Services;
using GlossaAI.Desktop.ViewModels;
using GlossaAI.Desktop.Views;
using GlossaAI.Infrastructure.Configuration;
using GlossaAI.Infrastructure.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Whisper.net.Ggml;

namespace GlossaAI.Desktop;

public partial class App : Application
{
    public IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        Services = serviceCollection.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainViewModel = Services.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.Configure<WhisperOptions>(options =>
        {
            options.ModelDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GlossaAI",
                "Models"
            );
            options.ModelType = GgmlType.Base;
            options.Language = "en";
        });

        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddHttpClient();

        var configService = new ConfigurationService();
        var settings = configService.LoadSettingsAsync().GetAwaiter().GetResult();
        services.AddSingleton(configService);
        services.AddSingleton(settings);
        
        services.AddSingleton<ISTTProvider, WhisperNetProvider>();
        services.AddSingleton<IAudioEngine, WasapiAudioEngine>();
        services.AddSingleton<IVideoProcessor, FFmpegVideoProcessor>();
        
        services.AddSingleton<OllamaProvider>();
        services.AddSingleton<OpenAiProvider>();
        services.AddSingleton<ILLMProviderFactory, LlmProviderFactory>();

        services.AddSingleton<MeetingManager>();
        services.AddSingleton<MainViewModel>();
    }

    public void OnShowApplicationClick(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            desktop.MainWindow.Show();
            desktop.MainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
            desktop.MainWindow.Activate();
        }
    }

    public void OnStartRecordingClick(object? sender, EventArgs e)
    {
        var vm = Services?.GetService<MainViewModel>();
        if (vm != null && vm.StartRecordingCommand.CanExecute(null))
        {
            vm.StartRecordingCommand.Execute(null);
        }
    }

    public void OnStopRecordingClick(object? sender, EventArgs e)
    {
        var vm = Services?.GetService<MainViewModel>();
        if (vm != null && vm.StopRecordingCommand.CanExecute(null))
        {
            vm.StopRecordingCommand.Execute(null);
        }
    }

    public void OnExitClick(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow is MainWindow mainWindow)
            {
                mainWindow.RealClose();
            }
            else
            {
                desktop.Shutdown();
            }
        }
    }
}