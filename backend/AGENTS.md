# Backend F1 Race Lens

- ASP.NET Core Minimal API, .NET 10. Код групуємо за функціональністю.
- Публічні DTO не залежать від структури зовнішнього API. Зовнішні дані валідуємо на межі адаптера.
- Час зберігаємо як `DateTimeOffset` у UTC, дати без часу — `DateOnly`. Відсутній час залишається `null`.
- Використовуємо `IHttpClientFactory`, таймаут і `CancellationToken`. Не передаємо користувацькі URL зовнішньому клієнту.
- Помилки API повертаємо як Problem Details без внутрішніх винятків і секретів.
- Перевірки: `dotnet build backend/F1RaceLens.slnx` і `dotnet test backend/F1RaceLens.slnx` з кореня проєкту.
- Запуск: `dotnet run --project backend/F1RaceLens.Api --launch-profile http`.

- PostgreSQL/EF Core: @context/architecture/postgresql-storage.md. Зміни схеми лише через міграції, без EnsureCreated у runtime. Спортивні GET читають опубліковані зрізи; невдалий імпорт не замінює попередню версію.
- PostgreSQL-тести використовують `RACELENS_TEST_DATABASE` і тільки створені ними тимчасові схеми. Без змінної інтеграційні сценарії мають явний skip. Не виводити значення змінної або локальний конфіг у логах.
