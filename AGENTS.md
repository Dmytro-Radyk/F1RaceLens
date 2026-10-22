# F1 Race Lens

## Файли контексту
Прочитай наступні файли щоб розуміти контекст проєкту

@context/project-overview.md - опис продукту
@context/coding-standards.md - правила написання коду
@context/ai-interaction.md - опис процесу роботи
@context/current-feature.md - поточна задача яку реалізовуємо

## Команди

Для запуску клієнтського застосунку з директорії frontend використовуй npm run start 

Backend: з кореня репозиторію `dotnet run --project backend/F1RaceLens.Api --launch-profile http`.
Для сторінок із даними API й frontend мають працювати одночасно. Початкова сторінка — `http://localhost:4200/home`. Контракт календаря: @context/architecture/calendar-api.md.
Перевірки backend: `dotnet build backend/F1RaceLens.slnx` і `dotnet test backend/F1RaceLens.slnx`.

Окремий LightGBM-експеримент і переглядач на порту 4300: @ml/README.md.
ML-команди запускати з директорії `ml`; перевірки: `.venv/Scripts/python.exe -m unittest discover -s tests -q`.
Ваги, сирі дані, кеші й локальні середовища не додавати в Git. На /predictions доступні скрипт і збережений LightGBM v4 через backend; журнал у PostgreSQL: @context/features/integrated-forecasts.md. Переглядач 4300 лишається окремим технічним інструментом.


## Тимчасовий F1-асистент

`/assistant`: локальна Qwen3-4B-Instruct-2507 через llama.cpp на loopback 4310. Одноразово `./scripts/assistant/Setup.ps1`, потім `./scripts/assistant/Start.ps1`; `Stop.ps1` завершує лише власний runtime. CPU-режим: `Start.ps1 -Cpu`. Контракт: @context/features/f1-assistant.md. Ваги/runtime у `.local/assistant` поза Git. Не зберігати й не логувати повідомлення; історія тільки у RAM вкладки до pagehide/виходу. Це окрема мовна модель; LightGBM не змінювати через задачі чату.

## PostgreSQL

Оновлення даних і двох прогнозів однією локальною командою: `./scripts/Update-Forecasts.ps1`. Контракт: @context/features/forecast-update-workflow.md. Не запускати цикли паралельно; це перерахунок на чинних вагах, не навчання чи фоновий розклад.

Перед API: `./scripts/database/Start.ps1`; перше встановлення Windows — `./scripts/database/Setup.ps1`. Контракт: @context/architecture/postgresql-storage.md.
Міграції: `dotnet run --project backend/F1RaceLens.Api --no-launch-profile -- --migrate`; готовність: `/health/storage`.
Ніколи не друкувати connection string або пароль; `.local/`, кластер, дампи та `appsettings.Local.json` виключені з Git. Коміт лише за командою користувача.
