# Тёмная / светлая тема AutoRAW — состояние (2026-05-15)

Краткая «точка возврата» по **системе тем** приложения.  
**Чат Zona** ведётся отдельно → [`zona-chat-status.md`](zona-chat-status.md) (работа приостановлена).

---

## ✅ Сделано (тема)

### Две темы + переключение

| Что | Где |
|-----|-----|
| Словари ресурсов | `Themes/Dark.xaml`, `Themes/Light.xaml`, `Themes/DarkInteractive.xaml` |
| Переключение Light / Dark / «как в системе» | `Services/ThemeService.cs` |
| Сохранение выбора | `Services/ThemePreferenceStore.cs` → `%AppData%\AutoRAW\theme_prefs.json` |
| Меню «Вид → Тема» | `MainWindow.xaml` + `MainViewModel` (`UiTheme`, `IsThemeMenu*`) |
| Системная тема Windows 10/11 | реестр `AppsUseLightTheme` + `UserPreferenceChanged` при режиме System |
| Глобальные кисти `Theme.*` | `DynamicResource` по окнам и диалогам |

### Палитра ТЗ (тёмная, `Dark.xaml`)

| Ключ ТЗ | Цвет | Theme.* |
|---------|------|---------|
| BgPrimary | `#1E1E1E` | WindowBg, PreviewSurface |
| BgSecondary | `#252526` | MenuBarBg, MenuPopupBg |
| BgPanel | `#2D2D30` | CardBg, GroupBoxBg, ListSurface |
| BgInput | `#3C3C3C` | InputBackground, ButtonSecondaryBg |
| AccentBlue | `#0078D4` | Accent, BorderActive |
| TextPrimary | `#F0F0F0` | TextPrimary |
| TextSecondary | `#A0A0A0` | TextSecondary, TextMuted |
| Border | `#404040` | CardBorder, InputBorder |

Светлая тема: те же ключи `Theme.*`, значения в `Light.xaml` (фон `#F2F3F5`, карточки белые и т.д.).

### ОС и хром окон

| Что | Где |
|-----|-----|
| Тёмная **системная шапка** (DWM) | `Services/Win11WindowChrome.cs` |
| Применение ко всем окнам при загрузке и смене темы | `App.xaml.cs` (`ThemeService.ThemeChanged`, `Window.Loaded`) |

### Интерактив и контролы (`DarkInteractive.xaml`)

- Меню / `MenuItem` / `ContextMenu` — тёмный popup, читаемый текст (`SystemColors` + шаблоны).
- Вторичные кнопки — hover, disabled (согласовано с primary в `MainWindow.xaml`).
- **GroupBox** — заголовок **внутри** рамки, разделитель (исправлен вылезающий header / белые края).
- Скроллбар — кастомный `PART_Track`, вертикаль / горизонталь.
- Слайдер — тёмный трек, акцент на thumb (`Theme.Slider*`).
- `ListBox`, чекбоксы, `ComboBoxItem` — под тёмную палитру.

### Чат Zona (только цвета фона в теме)

В `Dark.xaml` / `Light.xaml` добавлены кисти чата (`Theme.LogSurface`, `Theme.Bubble*`, `Theme.ChatBubbleText` …).  
Вёрстка и логика чата — **не в этом файле**, см. `zona-chat-status.md`.

---

## ⚠️ Частично / спорно

- **Плавная анимация** смены темы — не делали (мгновенная подмена `MergedDictionaries`).
- **Светлая тема** — словарь есть, но основной полиш делался под dark + референс Telegram для чата.
- **Субъективно** главное окно всё ещё может казаться «не Win11» (отступы, скругления, Mica) — см. идеи ниже.

---

## 📋 Панель Camera Raw / Photoshop (отдельный план)

Референс: боковая панель **«Редактировать»** (Свет, Цвет, Эффекты, слайдеры, гистограмма).

**Подробно — варианты A–F, фазы, чеклист:** [`camera-raw-panel-plan.md`](camera-raw-panel-plan.md)

| Вариант | Суть | Когда |
|---------|------|--------|
| **A** | Чистый WPF: свои `DevelopSlider` + `Expander`, стыковка с `ColorCorrectionSettings` | ✅ рекомендуем старт |
| **B** | WPF UI / MaterialDesign / HandyControl для слайдеров | если A затянется |
| **C** | OxyPlot / OpenCV — гистограмма | фаза 3–4 |
| **D** | Live preview через Magick.NET (уже есть) + debounce | фаза 1 |
| **E** | XMP импорт/экспорт, сетка профилей | частично есть |
| **F** | WinUI 3 — полная миграция | только при переписывании app |

**Уже в коде:** `ColorCorrectionSettings`, `ColorCorrectionService`, `XmpSettingsParser`, `ColorProfileEditorDialog` (без CR-панели).

---

## 📋 Бэклог темы (когда вернёмся)

- Единый **ControlTemplate** для `GroupBox` / карточек (WinUI-подобные скругления).
- Общий шаблон кнопок с `CornerRadius` и высотами.
- Слайдер: заливка пройденного участка акцентом.
- Меню: при «протекании» Aero — полный шаблон `MenuItem` / `Popup`.
- Опционально: Mica / `DwmWindowBackdrop` (отдельная задача).
- Плавный cross-fade при смене темы (storyboard на корневой панели или двойной слой).

---

## Где лежит тема (шпаргалка)

| Файл | Роль |
|------|------|
| `Themes/Dark.xaml` | Палитра dark, `Theme.*`, базовые `TextBox` / `ComboBox`, кисти чата |
| `Themes/Light.xaml` | Палитра light |
| `Themes/DarkInteractive.xaml` | SystemColors, Menu, ListBox, ScrollBar, Slider, GroupBox template |
| `Services/ThemeService.cs` | Подключение словарей, System theme, событие `ThemeChanged` |
| `Services/ThemePreferenceStore.cs` | JSON настроек |
| `Services/Win11WindowChrome.cs` | DWM immersive dark mode для title bar |
| `App.xaml` | Merged: `LogLineItemTemplate`, `ZonaChatResources` (не тема, но глобально) |
| `MainWindow.xaml` | Локальные стили кнопок, `DynamicResource Theme.*` |

---

## Как откатиться

Коммит с датой + этот файл; `git restore` по путям из таблицы.
