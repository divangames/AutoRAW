# Презентация AutoRAW (GitHub Pages)

Публичный лендинг: **https://divangames.github.io/AutoRAW/**

## Один раз в репозитории на GitHub

1. **Settings** → **Pages**
2. **Build and deployment** → **Source:** **GitHub Actions** (не «Deploy from branch»)
3. Сохранить

## Деплой

- Автоматически при push в `main`, если менялись файлы в `presentation/`
- Вручную: **Actions** → **Deploy presentation** → **Run workflow**
- Локально: `presentation\open.bat` → пункт **2** (или `deploy.ps1` без `deploy.env`)

## Локальный просмотр

`presentation\open.bat` → пункт **1** — сервер `http://127.0.0.1:8765/`
