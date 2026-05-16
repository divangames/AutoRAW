using AutoRAW.Models;
using CommunityToolkit.Mvvm.Input;
using WpfClipboard = System.Windows.Clipboard;

namespace AutoRAW.ViewModels;

public sealed class LogLineViewModel
{
    public LogLineViewModel(string text, LogLineKind kind = LogLineKind.Normal, bool fromZona = false)
    {
        Kind = kind;
        FromZona = fromZona || kind == LogLineKind.Zona;
        Text = EnsureTimestamp(text);
        (TimeStamp, Body) = SplitTimestamp(Text);
        TimeShort = ToTimeShort(TimeStamp);
        PlainText = Text.Replace("**", string.Empty);
        CopyCommand = new RelayCommand(() =>
        {
            if (!string.IsNullOrEmpty(PlainText))
                WpfClipboard.SetText(PlainText);
        });
    }

    public string Text { get; }

    /// <summary>Полная метка времени [HH:mm:ss] (копирование, лог).</summary>
    public string TimeStamp { get; }

    /// <summary>Время в углу пузыря, как в Telegram (HH:mm).</summary>
    public string TimeShort { get; }

    /// <summary>Текст сообщения без префикса времени.</summary>
    public string Body { get; }

    public string PlainText { get; }

    public LogLineKind Kind { get; }

    /// <summary>Показывать круглый аватар ZONA (реплики персонажа, в т.ч. пауза/продолжение).</summary>
    public bool FromZona { get; }

    public IRelayCommand CopyCommand { get; }

    private static string EnsureTimestamp(string text)
    {
        if (TryParseTimestamp(text, out _, out _))
            return text;

        return $"[{DateTime.Now:HH:mm:ss}] {text}";
    }

    private static (string TimeStamp, string Body) SplitTimestamp(string text)
    {
        if (TryParseTimestamp(text, out var stamp, out var body))
            return (stamp, body.Replace("**", string.Empty));

        var now = $"[{DateTime.Now:HH:mm:ss}]";
        return (now, text.Replace("**", string.Empty));
    }

    private static string ToTimeShort(string stamp)
    {
        if (stamp.Length >= 7 && stamp[0] == '[')
        {
            var close = stamp.IndexOf(']');
            if (close > 1)
            {
                var inner = stamp[1..close];
                return inner.Length >= 5 ? inner[..5] : inner;
            }
        }

        return DateTime.Now.ToString("HH:mm");
    }

    private static bool TryParseTimestamp(string text, out string stamp, out string body)
    {
        stamp = string.Empty;
        body = text;

        if (text.Length < 11 || text[0] != '[')
            return false;

        var close = text.IndexOf(']');
        if (close is < 9 or > 11)
            return false;

        var inner = text[1..close];
        if (inner.Length != 8 || inner[2] != ':' || inner[5] != ':')
            return false;

        stamp = text[..(close + 1)];
        body = text[(close + 1)..].TrimStart();
        return true;
    }
}
