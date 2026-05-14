using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using AutoRAW.Services;

namespace AutoRAW;

public partial class App : System.Windows.Application
{
    private const int MinimumSplashMs = 2000;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Пока виден только сплэш, закрытие единственного окна не должно завершать процесс.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _ = StartupSequenceAsync();
    }

    private async Task StartupSequenceAsync()
    {
        var splash = new SplashWindow();
        splash.Show();
        splash.SetStatus("Запуск…");
        splash.UpdateLayout();
        var sw = Stopwatch.StartNew();

        try
        {
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

            var remain = MinimumSplashMs - (int)sw.ElapsedMilliseconds;
            if (remain > 0)
                await Task.Delay(remain);

            await Dispatcher.InvokeAsync(() =>
            {
                main!.Show();
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                splash.Close();
            }, DispatcherPriority.Normal);
        }
        catch (Exception ex)
        {
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
