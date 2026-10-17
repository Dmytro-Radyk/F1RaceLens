# Frontend F1 Race Lens

Angular 22, TypeScript, signals, Signal Forms і Vitest. Календар та сторінки Гран-прі доступні без входу, дані надходять із власного ASP.NET Core API.

## Запуск

Потрібні Node.js 24.15.0 або новіша сумісна версія 24.x та npm 11.

Спочатку в окремому терміналі з кореня репозиторію запустіть API (.NET 10 SDK):

```powershell
dotnet run --project backend/F1RaceLens.Api --launch-profile http
```

З директорії `frontend`:

```powershell
npm ci
npm run start
```

Відкрийте <http://localhost:4200/calendar>. Development-проксі з `proxy.conf.json` спрямовує `/api/**` на <http://127.0.0.1:5080>.

## Сторінки

- `/calendar` — поточний сезон, найближчий Гран-прі та фільтри етапів.
- `/calendar/{season}/{round}` — траса й розклад фактичних сесій етапу.
- `/login` — демонстраційні форми входу й реєстрації. Демо-вхід: `admin` / `12345`.

Інші розділи мають явні повідомлення про майбутню реалізацію. Реєстрація, зовнішня авторизація та відновлення пароля ще не підключені до сервера.

## Перевірки

```powershell
npm run build
npm test -- --watch=false
```

Тести охоплюють форми, тему, календарні маршрути, фільтри, відсутній час, помилки й повторні спроби.

Production-збірка міститься у `dist/frontend`. На хостингу потрібно налаштувати fallback на `index.html` для маршрутів Angular та reverse proxy `/api` до бекенду: development-проксі в production не працює.

Дивіться також [головний README](../README.md), [правила frontend](AGENTS.md), [дизайн](../context/design/ui-guidelines.md) та [контракт API](../context/architecture/calendar-api.md).
