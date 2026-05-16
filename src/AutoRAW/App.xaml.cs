using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using AutoRAW.Services;

namespace AutoRAW;

public partial class App : System.Windows.Application
{
    /// <summary>Минимум времени сплэша в полной непрозрачности после анимации появления (10 с = 10000 мс).</summary>
    private const int MinimumSplashVisibleMs = 10000;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeService.Initialize(this);

        // Тёмная шапка окна для всех окон, создаваемых в приложении.
        EventManager.RegisterClassHandler(
            typeof(Window),
            Window.LoadedEvent,
            new RoutedEventHandler(OnAnyWindowLoaded));

        ThemeService.ThemeChanged += OnGlobalThemeChanged;

        // Пока виден только сплэш, закрытие единственного окна не должно завершать процесс.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _ = StartupSequenceAsync();
    }

    private static void OnAnyWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window w)
            Win11WindowChrome.SetTitleBarDark(w, ThemeService.CurrentIsDark);
    }

    private void OnGlobalThemeChanged(bool isDark)
    {
        foreach (Window w in Windows)
            Win11WindowChrome.SetTitleBarDark(w, isDark);
    }

    private static Task RunOnSplashDispatcherAsync(SplashWindow splash, Func<Task> action) =>
        splash.Dispatcher.InvokeAsync(action).Task.Unwrap();

    private async Task StartupSequenceAsync()
    {
        var splash = new SplashWindow();
        splash.Show();
        splash.SetStatus("Запуск…");
        splash.UpdateLayout();

        try
        {
            await RunOnSplashDispatcherAsync(splash, splash.PlayFadeInAsync);

            var sw = Stopwatch.StartNew();

            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Task.Delay(30);

            await Dispatcher.InvokeAsync(() => splash.SetStatus("Загрузка интерфейса…"), DispatcherPriority.Normal);
            await Task.Yield();

            MainWindow? main = null;
            await Dispatcher.InvokeAsync(() =>
            {
                splash.SetStatus("Подготовка главного окна…");
                main = new MainWindow();
                MainWindow = main;
            }, DispatcherPriority.Normal);

            await Dispatcher.InvokeAsync(() => splash.SetStatus("Готово"), DispatcherPriority.Normal);

            var remain = MinimumSplashVisibleMs - (int)sw.ElapsedMilliseconds;
            if (remain > 0)
                await Task.Delay(remain);

            await RunOnSplashDispatcherAsync(splash, splash.PlayFadeOutAsync);

            await Dispatcher.InvokeAsync(() =>
            {
                main!.Show();
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                splash.Close();
            }, DispatcherPriority.Normal);
        }
        catch (Exception ex)
        {
            try
            {
                if (splash.Opacity > 0.01)
                    await RunOnSplashDispatcherAsync(splash, splash.PlayFadeOutAsync);
            }
            catch
            {
                /* ignore */
            }

            splash.Close();
            System.Windows.MessageBox.Show(
                ex.Message,
                $"AutoRAW — {AppMetadata.DisplayVersion}",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
