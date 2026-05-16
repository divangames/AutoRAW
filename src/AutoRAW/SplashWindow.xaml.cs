using System.Windows;
using System.Windows.Media.Animation;
using AutoRAW.Services;

namespace AutoRAW;

public partial class SplashWindow : Window
{
    private const int FadeInMs = 520;
    private const int FadeOutMs = 480;

    public SplashWindow()
    {
        InitializeComponent();
        Opacity = 0;
        VersionLine.Text = $"Версия {AppMetadata.DisplayVersion}";
        Closed += SplashWindow_Closed;
    }

    private void SplashWindow_Closed(object? sender, EventArgs e)
    {
        Closed -= SplashWindow_Closed;
        StatusProgressBar.IsIndeterminate = false;
    }

    public void SetStatus(string text) => StatusBarText.Text = text;

    /// <summary>Плавное появление окна (Opacity 0 → 1).</summary>
    public Task PlayFadeInAsync()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(FadeInMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
        anim.Completed += (_, _) => tcs.TrySetResult();
        BeginAnimation(OpacityProperty, anim);
        return tcs.Task;
    }

    /// <summary>Плавное скрытие (Opacity 1 → 0) перед закрытием.</summary>
    public Task PlayFadeOutAsync()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var anim = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(FadeOutMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.HoldEnd,
        };
        anim.Completed += (_, _) => tcs.TrySetResult();
        BeginAnimation(OpacityProperty, anim);
        return tcs.Task;
    }
}
