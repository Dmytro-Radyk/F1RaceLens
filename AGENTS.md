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
Для календаря API й frontend мають працювати одночасно. Контракт: @context/architecture/calendar-api.md.
Перевірки backend: `dotnet build backend/F1RaceLens.slnx` і `dotnet test backend/F1RaceLens.slnx`.

