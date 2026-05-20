# Презентация AutoRAW (GitHub Pages)

Публичный лендинг: **https://divangames.github.io/AutoRAW/**

## Один раз в репозитории на GitHub

**Settings** → **Pages** → **Build and deployment**:

| Вариант | Настройка |
|--------|-----------|
| **Рекомендуется** | **Source:** Deploy from a branch → Branch: **`gh-pages`** → Folder: **`/ (root)`** |
| Альтернатива | **Source:** GitHub Actions (workflow сам попробует включить Pages) |

После первого успешного workflow подождите 1–2 минуты и откройте сайт.

## Деплой

- Автоматически при push в `main`, если менялись файлы в `presentation/`
- Вручную: **Actions** → **Deploy presentation** → **Run workflow**
- Локально: `presentation\open.bat` → пункт **2** (или `deploy.ps1` без `deploy.env`)

## Локальный просмотр

`presentation\open.bat` → пункт **1** — сервер `http://127.0.0.1:8765/`
