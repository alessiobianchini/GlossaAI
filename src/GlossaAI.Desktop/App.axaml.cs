using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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

        Dispatcher.UIThread.UnhandledException += (sender, args) =>
        {
            LogException(args.Exception, "Dispatcher.UIThread.UnhandledException");
            args.Handled = false;
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainViewModel = Services.GetRequiredService<MainViewModel>();
            var window = new MainWindow { DataContext = mainViewModel };
            desktop.MainWindow = window;
            window.Show();
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

        services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName, client => 
        {
            client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        });

        var configService = new ConfigurationService();
        var settings = configService.LoadSettings();
        services.AddSingleton(configService);
        services.AddSingleton(settings);
        services.AddSingleton<UpdateService>();
        
        services.AddSingleton<ISTTProvider, WhisperNetProvider>();
        services.AddSingleton<IAudioEngine, WasapiAudioEngine>();
        services.AddSingleton<IVideoProcessor, FFmpegVideoProcessor>();
        
        services.AddSingleton<OllamaProvider>();
        services.AddSingleton<OpenAiProvider>();
        services.AddSingleton<ILLMProviderFactory, LlmProviderFactory>();
        services.AddSingleton<HistoryService>();

        services.AddSingleton<MeetingManager>();
        services.AddSingleton<MainViewModel>();
    }

    private void LogException(Exception? ex, string context)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GlossaAI");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "crash.log");
            File.AppendAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{context}] {ex}\n\n");
        }
        catch { }
    }

    public void OnShowApplicationClick(object? sender, EventArgs e)
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

                    if (desktop.MainWindow == null)
                    {
                        var mainViewModel = Services?.GetService(typeof(MainViewModel)) as MainViewModel;
                        if (mainViewModel != null)
                        {
                            desktop.MainWindow = new MainWindow { DataContext = mainViewModel };
                        }
                    }

                    desktop.MainWindow?.Show();
                    if (desktop.MainWindow != null)
                    {
                        desktop.MainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
                        desktop.MainWindow.Activate();
                    }
                }
                catch (Exception ex)
                {
                    LogException(ex, "OnShowApplicationClick Action");
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            LogException(ex, "OnShowApplicationClick Outer");
            throw;
        }
    }

    public void OnStartRecordingClick(object? sender, EventArgs e)
    {
        try
        {
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    var vm = Services?.GetService<MainViewModel>();
                    if (vm != null && vm.StartRecordingCommand.CanExecute(null))
                    {
                        await vm.StartRecordingCommand.ExecuteAsync(null);
                    }
                }
                catch (Exception ex)
                {
                    LogException(ex, "OnStartRecordingClick Action");
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            LogException(ex, "OnStartRecordingClick Outer");
            throw;
        }
    }

    public void OnStopRecordingClick(object? sender, EventArgs e)
    {
        try
        {
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    var vm = Services?.GetService<MainViewModel>();
                    if (vm != null && vm.StopRecordingCommand.CanExecute(null))
                    {
                        await vm.StopRecordingCommand.ExecuteAsync(null);
                    }
                }
                catch (Exception ex)
                {
                    LogException(ex, "OnStopRecordingClick Action");
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            LogException(ex, "OnStopRecordingClick Outer");
            throw;
        }
    }

    public void OnExitClick(object? sender, EventArgs e)
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
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
                catch (Exception ex)
                {
                    LogException(ex, "OnExitClick Action");
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            LogException(ex, "OnExitClick Outer");
            throw;
        }
    }
}