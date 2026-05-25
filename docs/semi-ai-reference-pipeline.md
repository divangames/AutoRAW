# Semi-AI: эталон reference → подгонка RAW

## Источник истины

| Роль | Путь (пример «Кроссовки») |
|------|---------------------------|
| Эталон композиции | `reference\Sneakers\01.jpg` … `08.jpg` |
| Исходник | `test\RAW\46741\8.NEF` и т.д. |
| **Не используется** для подгонки | `zona\Sneakers\operation\` |

## Пайплайн в коде

1. **`ReferenceCompositionCatalog`** — при анализе `reference\NN.jpg` сохраняет `ReferenceCompositionTemplate` (центр, размер, отступы в долях кадра).
2. **`SubjectDetectionService`** — находит товар на RAW (и на эталоне при построении шаблона).
3. **`ManualShotAutoAlignService`** — подбирает `ManualShotAdjust` (смещение, масштаб %), чтобы в кадре размера референса товар совпал с шаблоном.
4. **`ManualShotAdjustApplier`** — сборка превью/экспорта (cover + pan/zoom).
5. **`ManualShotFrameResolver`** — сохранённые правки json → иначе авто по шаблону.

## UI

- Главное окно: превью **После (авто)** / **После (экспорт)**, очередь с ✓/⚠.
- Редактор: **Авто-подгонка**, ручные ползунки, сохранение в `manual_shot_adjust.json`.

## Модели (опционально)

- `models/subject/yolov8n.onnx` — `bat/download-yolov8n-onnx.ps1`
- `models/subject/u2netp.onnx` — `bat/download-u2netp-onnx.ps1`
