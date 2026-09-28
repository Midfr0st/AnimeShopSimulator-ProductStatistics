# Статистика товаров — Anime Shop Simulator

![Anime Shop Simulator](https://img.shields.io/badge/Anime%20Shop%20Simulator-1.0.6-f6a800)
![Version](https://img.shields.io/badge/version-0.5.1-1685d1)
![WolfCore](https://img.shields.io/badge/requires-WolfCore-1685d1)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3-7952b3)
![License](https://img.shields.io/badge/license-MIT-2ea44f)

**Статистика товаров** добавляет в игровой терминал отдельную страницу с продажами каждого товара.

Мод помогает понять, что покупают чаще, какие товары приносят больше денег и что стоит заказывать в большем количестве.

## Скачать

Готовая сборка находится в **[последнем выпуске](https://github.com/Midfr0st/AnimeShopSimulator-ProductStatistics/releases/latest)**.

Для работы необходим [WolfCore](https://github.com/Midfr0st/AnimeShopSimulator-WolfCore).

## Возможности

- проданное количество по каждому товару;
- фактическая выручка;
- расчётная валовая прибыль;
- периоды `Текущий день`, `Прошлый день` и `Всё время`;
- сортировка по товару, количеству продаж и прибыли;
- прокручиваемая таблица с изображениями товаров;
- автоматическое обнаружение новых товаров и DLC.

Плитка `Статистика` добавляется на главный экран игрового терминала через WolfCore.

## Установка

1. Полностью закройте игру.
2. Установите [MelonLoader](https://github.com/LavaGang/MelonLoader).
3. Установите [WolfCore](https://github.com/Midfr0st/AnimeShopSimulator-WolfCore/releases/latest).
4. Скачайте `WolfProductStatistics.dll` из [Releases](https://github.com/Midfr0st/AnimeShopSimulator-ProductStatistics/releases/latest).
5. Поместите обе DLL в папку `Anime Shop Simulator\Mods`.
6. Запустите игру и откройте игровой терминал.

Подробности: [INSTALLATION.ru.md](docs/INSTALLATION.ru.md).

## Данные пользователя

```text
Anime Shop Simulator\UserData\WolfProductStatistics.json
Anime Shop Simulator\UserData\WolfProductStatistics.settings.json
```

Игра не хранит полную историю старых чеков, поэтому статистика начинает накапливаться после установки мода.

## Совместимость

- Anime Shop Simulator `1.0.6`;
- MelonLoader `0.7.3`;
- WolfCore `0.2.5`;
- Windows x64, Unity IL2CPP.

## Если что-то не работает

См. [решение проблем](docs/TROUBLESHOOTING.ru.md). Для отчёта приложите `MelonLoader\Latest.log` и создайте обращение в [GitHub Issues](https://github.com/Midfr0st/AnimeShopSimulator-ProductStatistics/issues).

## Связанные проекты

- [WolfCore](https://github.com/Midfr0st/AnimeShopSimulator-WolfCore) — обязательное ядро и меню настроек;
- [Фильтры полок](https://github.com/Midfr0st/AnimeShopSimulator-ShelfFilters);
- [Отзывы о магазине](https://github.com/Midfr0st/AnimeShopSimulator-ShopReviews);
- [Расписание работников](https://github.com/Midfr0st/AnimeShopSimulator-EmployeeSchedules).

## Лицензия

Проект распространяется по условиям [MIT License](LICENSE). Это неофициальная пользовательская модификация. Мод предоставляется «как есть» и используется на свой риск.
