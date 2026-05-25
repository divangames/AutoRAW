# Модели детекции товара (ONNX)

Положите файлы в **`models/subject/`** (рядом с `AutoRAW.exe` после сборки: `models/subject/…`).

| Файл | Назначение |
|------|------------|
| **`yolov8n-seg.onnx`** | Сегментация + bbox (приоритет для силуэта и авто-подгонки) |
| **`yolov8n.onnx`** | Детекция bbox (fallback, если seg нет) |
| **`u2netp.onnx`** | Уточнение контура внутри bbox YOLO-detect |

## Скачать / собрать ONNX

В релизах **ultralytics/assets** нет готовых `*.onnx` (только `.pt`) — скрипты **экспортируют** через Python.

Нужен **Python 3** (`py`, `python` или `python3` в PATH).

Из корня репозитория:

```powershell
.\bat\download-yolov8n-seg-onnx.ps1
.\bat\download-yolov8n-onnx.ps1
.\bat\download-u2netp-onnx.ps1
```

Первый запуск seg: `pip install ultralytics`, скачивание `yolov8n-seg.pt`, экспорт в `models\subject\yolov8n-seg.onnx` (~6–7 MB).

Вручную:

```powershell
py -3 -m pip install ultralytics onnx
py -3 -c "from ultralytics import YOLO; YOLO('yolov8n-seg.pt').export(format='onnx', imgsz=640, simplify=True, opset=12)"
# скопируйте yolov8n-seg.onnx в models\subject\
```

После скачивания **пересоберите** (`bat\build.bat` или F5) — `*.onnx` копируются в `bin\…\models\subject\`.

### Ошибки

| Симптом | Решение |
|--------|---------|
| HTTP 404 на GitHub | Нормально: используйте экспорт через скрипт (Python + ultralytics). |
| Нет Python | Установите [python.org](https://www.python.org/downloads/) с галочкой «Add to PATH». |
| pip / ultralytics | `py -3 -m pip install -U ultralytics onnx` |

## RAW (LibRaw)

В главном окне: галочка **«Открывать NEF/RAW через LibRaw»** — настройка в `%AppData%\AutoRAW\raw_loader_prefs.json`. Нужны NuGet `Sdcb.LibRaw` + `Sdcb.LibRaw.runtime.win64` (уже в проекте). При ошибке LibRaw используется ImageMagick.

## GPU (Windows)

**Microsoft.ML.OnnxRuntime.DirectML** — YOLO/U2Net через DirectML, иначе CPU. Провайдер виден в статусе авто-подгонки в редакторе.

## Если ONNX нет

**OpenCV** (`SubjectBoundsEstimator`) — редактор и пакетная обработка работают без моделей.

## Лицензия

YOLOv8 — [Ultralytics AGPL-3.0](https://github.com/ultralytics/ultralytics/blob/main/LICENSE).
