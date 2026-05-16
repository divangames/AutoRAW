using System.Windows;
using AutoRAW.Models;
using Microsoft.Win32;
using WpfApplication = System.Windows.Application;

namespace AutoRAW.Services;

/// <summary>Подключение словаря Light/Dark к Application, режим «как в системе» и реакция на смену темы Windows.</summary>
public static class ThemeService
{
    private static readonly Uri LightDictionaryUri = new("pack://application:,,,/Themes/Light.xaml", UriKind.Absolute);
    private static readonly Uri DarkDictionaryUri = new("pack://application:,,,/Themes/Dark.xaml", UriKind.Absolute);
    private static readonly Uri DarkInteractiveUri = new("pack://application:,,,/Themes/DarkInteractive.xaml", UriKind.Absolute);

    private static WpfApplication? _app;
    private static AppUiTheme _activePreference = AppUiTheme.Light;
    private static UserPreferenceChangedEventHandler? _systemHandler;

    /// <summary>Текущая эффективная тема (true = тёмная). Обновляется при каждом <see cref="ApplyInternal"/>.</summary>
    public static bool CurrentIsDark { get; private set; }

    /// <summary>Срабатывает при каждой смене эффективной темы (аргумент: true — тёмная).</summary>
    public static event Action<bool>? ThemeChanged;

    public static void Initialize(WpfApplication app)
    {
        _app = app;
        _activePreference = ThemePreferenceStore.Get();
        ApplyInternal(app, _activePreference);
        UpdateSystemWatcher();
    }

    /// <summary>Вызывается из ViewModel при смене темы пользователем.</summary>
    public static void ApplyUserPreference(WpfApplication app, AppUiTheme preference)
    {
        _app = app;
        _activePreference = preference;
        ThemePreferenceStore.Set(preference);
        ApplyInternal(app, preference);
        UpdateSystemWatcher();
    }

    private static void UpdateSystemWatcher()
    {
        if (_systemHandler is not null)
        {
            SystemEvents.UserPreferenceChanged -= _systemHandler;
            _systemHandler = null;
        }

        if (_activePreference != AppUiTheme.System)
            return;

        _systemHandler = OnUserPreferenceChanged;
        SystemEvents.UserPreferenceChanged += _systemHandler;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General)
            return;
        if (_activePreference != AppUiTheme.System || _app is null)
            return;

        _ = _app.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_activePreference == AppUiTheme.System && _app is not null)
                ApplyInternal(_app, AppUiTheme.System);
        }));
    }

    private static void ApplyInternal(WpfApplication app, AppUiTheme preference)
    {
        var useDark = preference switch
        {
            AppUiTheme.Dark => true,
            AppUiTheme.Light => false,
            AppUiTheme.System => !IsWindowsAppsLightTheme(),
            _ => false
        };

        var merged = app.Resources.MergedDictionaries;
        for (var i = merged.Count - 1; i >= 0; i--)
        {
            var src = merged[i].Source;
            if (src is null)
                continue;
            var s = src.ToString();
            if (s.Contains("/Themes/Light.xaml", StringComparison.OrdinalIgnoreCase)
                || s.Contains("/Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase)
                || s.Contains("/Themes/DarkInteractive.xaml", StringComparison.OrdinalIgnoreCase))
            {
                merged.RemoveAt(i);
            }
        }

        if (useDark)
        {
            merged.Add(new ResourceDictionary { Source = DarkDictionaryUri });
            merged.Add(new ResourceDictionary { Source = DarkInteractiveUri });
        }
        else
        {
            merged.Add(new ResourceDictionary { Source = LightDictionaryUri });
        }

        CurrentIsDark = useDark;
        ThemeChanged?.Invoke(useDark);
    }

    /// <summary>Реестр Windows: 1 — светлые приложения, 0 — тёмные.</summary>
    public static bool IsWindowsAppsLightTheme()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                writable: false);
            var v = k?.GetValue("AppsUseLightTheme");
            if (v is int i)
                return i != 0;
        }
        catch
        {
            /* ignore */
        }

        return true;
    }
}
