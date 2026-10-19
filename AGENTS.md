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
Ваги, сирі дані, кеші й локальні середовища не додавати в Git. Статистичний скрипт на 4200 та ML на 4300 поки не інтегровані.

