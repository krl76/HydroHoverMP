# 🚤 HydroHover MP

**Сетевой физический симулятор судна на воздушной подушке (СВП)**  
Многопользовательская гоночная аркада с процедурными волнами Герстнера, честной синхронизацией и сервер-авторитетной логикой.

Unity 6000.3.9f1 · FishNet 4.7.2 · Zenject · URP · C#

---

<p align="center">
  <img src="presentation/Screenshot_4.png" width="80%" alt="Общий план уровня" style="border-radius: 12px; box-shadow: 0 8px 32px rgba(0,0,0,0.3);" />
  <br/><em>Общий план игрового уровня — водная трасса с препятствиями</em>
</p>

## 📋 Содержание

- [О проекте](#о-проекте)
- [Технологический стек](#технологический-стек)
- [Игровые механики](#игровые-механики)
  - [Физика судна на воздушной подушке](#физика-судна-на-воздушной-подушке)
  - [Волны Герстнера](#волны-герстнера)
  - [Гидро-импульс](#гидро-импульс)
  - [Чекпойнты и гонка](#чекпойнты-и-гонка)
- [Мультиплеерные механики](#мультиплеерные-механики)
  - [Сетевая топология](#сетевая-топология)
  - [Синхронизация состояния (SyncVar / SyncList / SyncDictionary)](#синхронизация-состояния)
  - [RPC: запрос → валидация → трансляция](#rpc-поток)
  - [Владение и Owner/Remote-разделение](#владение-и-ownerremote-разделение)
  - [Жизненный цикл сессии](#жизненный-цикл-сессии)
  - [Dedicated Server](#dedicated-server)
  - [Синхронизированная вода](#синхронизированная-вода)
  - [Анти-чит](#анти-чит)
  - [Сетевой лидерборд](#сетевой-лидерборд)
- [Архитектура проекта](#архитектура-проекта)
- [Скриншоты](#скриншоты)
- [Быстрый старт](#быстрый-старт)
- [Сборка](#сборка)

---

## О проекте

**HydroHover MP** — сетевая гоночная аркада, в которой игроки управляют судами на воздушной подушке (hovercraft) над процедурно-волновой водной поверхностью. Участники проходят трассу из чекпойнтов; у каждого синхронизируются ник, HP, очки и время финиша.

Ключевое отличие от учебных примеров — **собственная физика подушки над синхронизированной поверхностью Герстнеровских волн**. Проект не повторяет стандартные сетевые демки: движение по волнам — нетривиальная задача синхронизации, решённая через комбинацию client-authoritative движения, защитных клампов и серверной валидации критических событий.

Проект выполнен в рамках курсовой/дипломной работы группы БСБО-09-23.

---

## Технологический стек

| Технология | Версия | Назначение |
|---|---|---|
| **FishNet** | 4.7.2 | Сетевой фреймворк (выбран вместо NGO — расширенные SyncType, встроенный `NetworkTransform`, Tugboat-транспорт) |
| **Zenject / Extenject** | — | Dependency Injection, вся архитектура проекта |
| **Addressables** | 2.8.1 | Асинхронная загрузка сцен, UI и ассетов |
| **UniTask** | — | async/await без аллокаций |
| **Cinemachine** | 2.10.5 | Камера владельца |
| **Input System** | 1.18.0 | Современный ввод |
| **URP** | 17.3.0 | Рендеринг + water-шейдеры |
| **Tugboat** | (FishNet) | Надёжный UDP-транспорт, порт 7770 |
| **ParrelSync** | — | Локальный запуск 2 клиентов |

---

## Игровые механики

### Физика судна на воздушной подушке

Сердце геймплея — **пружинно-демпферная модель подушки** (`HoverCushion`). Судно не касается воды: между днищем и поверхностью поддерживается воздушный зазор.

```
F = clamp((H_over - H_diff) / H_over) · K_spring · LiftEfficiency + (-v · C_damper)
```

- **Опорные точки** — несколько точек под корпусом, каждая считает свою высоту над водой
- **Сжатие пружины** — жёстко ограничено (`Mathf.Clamp01`), без клэмпа погружённая точка давала 3–4-кратное сжатие и «катапультировала» лодку
- **Демпфер** — гасит вертикальную скорость
- **LiftEfficiency** — масштабируется от оборотов нагнетателя (`HoverEngine` с инерционной моделью RPM)

**Двигатели:**
- **`HoverEngine`** (нагнетатель) — создаёт подъёмную силу, RPM с инерцией
- **`ThrustEngine`** (маршевый винт) — продольная тяга
- **`HoverAerodynamics`** — аэро/гидродинамическое сопротивление от скорости, руление

<details>
<summary>📐 Подробнее о расчёте подушки (тык)</summary>

```csharp
float waterHeight = _waterSystem.GetWaterHeightAt(point.position);
float heightDiff = point.position.y - waterHeight;
if (heightDiff < _hoverHeight)
{
    float compression = Mathf.Clamp01((_hoverHeight - heightDiff) / _hoverHeight);
    float springForce = _springForce * compression * LiftEfficiency;
    float verticalVelocity = _rb.GetPointVelocity(point.position).y;
    float dampingForce = -verticalVelocity * _damperForce;
    float totalForce = Mathf.Max(0, springForce + dampingForce);
    _rb.AddForceAtPosition(Vector3.up * totalForce, point.position);
}
```
</details>

---

### Волны Герстнера

Водная поверхность построена на **сумме двух волн Герстнера** — в отличие от простых синусоидальных волн, они смещают вершины не только по Y, но и по X/Z навстречу гребню, создавая острые пики и широкие впадины. Это критично для поведения судна: форма волны напрямую влияет на крен и дифферент корпуса.

```csharp
float k = 2π / wavelength;
float f = k · (dot(dir, pos.xz) - speed · time);
height = amplitude · sin(f);
```

Параметры волн (длина, скорость, направление, амплитуда) настраиваются через `WaveSettings` (ScriptableObject). Вода синхронизирована **по сетевому тику FishNet** — сервер и все клиенты видят одинаковую поверхность.

---

### Гидро-импульс

Уникальная способность судна — **рывок вперёд** с начислением бонусных очков. Реализует полный RPC-цикл:

1. **Клиент-владелец** нажимает кнопку — применяет импульс локально (мгновенный отклик) и отправляет `[ServerRpc]`
2. **Сервер** проверяет: жив ли игрок, не финишировал ли, в фазе ли Race, не вышел ли кулдаун
3. **Сервер начисляет очки** (+25) и рассылает `[ObserversRpc]` с эффектами (частицы + звук)
4. **Дедупликация** эффекта через sequence-номер

```
Клиент → [ServerRpc] → Сервер (валидация + SyncVar) → [ObserversRpc] → Все клиенты
         └─ локальный impulse (предсказание)
```

---

### Чекпойнты и гонка

Трасса состоит из последовательных чекпойнтов. Механика:

- Судно должно пройти чекпойнты **строго по порядку**
- «Перепрыг» через чекпойнт → штраф **-10 HP**, прогресс не засчитывается
- Каждый чекпойнт → **+100 очков**
- Финиш → **+500 очков**, запись времени

Двойная защита от читов:
1. **Серверная валидация** порядка чекпойнтов
2. **Клиентская проверка** `CheckpointTrigger` — знак скалярного произведения скорости на forward: игрок должен реально проехать **сквозь** триггер

---

## Мультиплеерные механики

### Сетевая топология

Проект использует **гибридную топологию** с автоматическим выбором режима:

| Режим | Описание |
|---|---|
| **Host** | Один игрок становится сервером + играет сам. Другие подключаются к нему как Client |
| **Client** | Подключается к существующему хосту или выделенному серверу |
| **Dedicated Server** | Headless-сервер без графики. Работает на Linux/Windows из командной строки |

Стартовое ветвление:
```
BootstrapState → IsDedicatedServer?
    ├─ true  → ServerBootstrapState (без UI, сразу старт сервера)
    └─ false → MainMenuState (кнопки Host / Client)
```

Хост = сервер (`ServerManager.StartConnection`) + локальный клиент (`ClientManager.StartConnection`). При сбое клиента сервер откатывается — атомарность подключения.

<p align="center">
  <img src="presentation/Screenshot_5.png" width="80%" alt="Два клиента в одной сессии" style="border-radius: 12px; box-shadow: 0 8px 32px rgba(0,0,0,0.3);" />
  <br/><em>Два клиента в одной игровой сессии</em>
</p>

---

### Синхронизация состояния

**`NetworkPlayerData`** — синхронизирует состояние каждого игрока через **FishNet SyncVar**:

| SyncVar | Тип | Назначение |
|---|---|---|
| `Nickname` | `string` | Отображаемый ник |
| `HP` | `int` | Здоровье (0–100) |
| `Score` | `int` | Очки за чекпойнты и финиш |
| `CheckpointIndex` | `int` | Текущий (пройденный) чекпойнт |
| `IsReady` | `bool` | Готовность к гонке |
| `IsFinished` | `bool` | Флаг финиша |
| `FinishTime` | `float` | Время финиша |

**Коллективные данные:**
- `SyncVar<SessionPhase> Phase` — фаза сессии (Lobby / Countdown / Race / Results)
- `SyncList<NetworkLeaderboardRecord> DedicatedLeaderboardRecords` — таблица рекордов
- `SyncDictionary<uint, PostRaceVote> PostRaceVotes` — голосование за рестарт

Запись — только на сервере. Клиенты подписываются на `OnChange` — UI обновляется реактивно.

**Оптимизация трафика: время гонки** не шлётся каждый кадр. Сервер раз встарте фиксирует `RaceStartTick` (`SyncVar<uint>`), клиенты сами вычисляют `elapsed = (currentTick - startTick) · tickDelta`. Один `uint` вместо 60 пакетов/c.

---

### RPC-поток

Два типа удалённых вызовов:

**`[ServerRpc]` — клиент → сервер:**
- `RequestHydroPulseServerRpc` — запрос на активацию способности
- `PassCheckpointServerRpc` — прохождение чекпойнта
- `SetNicknameServerRpc` — смена ника
- `SubmitPostRaceVoteServerRpc` — голос за рестарт

**`[ObserversRpc]` — сервер → все наблюдатели:**
- `PlayHydroPulseObserversRpc` — звук + VFX импульса (с дедубликацией)
- `ApplySpawnTransformObserversRpc` — телепорт на точку спавна

Сервер **всегда валидирует** входящие RPC перед применением.

---

### Владение и Owner/Remote-разделение

`NetworkHoverOwnerBridge` — центральный компонент, разделяющий поведение для владельца и удалённых клиентов:

| Аспект | Владелец (Owner) | Удалённые (Remote) |
|---|---|---|
| Физика | Полная симуляция (`Rigidbody` active) | `isKinematic = true` |
| Ввод | Читается (`InputService`) | Отключён |
| `HoverCushion` | Включён | Выключен |
| `HoverEngine` | Включён | Выключен |
| Камера | Активна | Отключена |
| Движение | Client-authoritative + `NetworkTransform` | Интерполяция + защитный кламп |

**Защитный кламп (height-clamp)** — для удалённых судов:
```csharp
float waterHeight = waterSystem.GetWaterHeightAt(position);
position.y = Mathf.Clamp(position.y, waterHeight - 0.6f, waterHeight + 2.5f);
```
Не даёт «улететь» или «утонуть» при сетевых задержках.

<p align="center">
  <img src="presentation/Screenshot_1.png" width="80%" alt="Движение волн Герстнера" style="border-radius: 12px; box-shadow: 0 8px 32px rgba(0,0,0,0.3);" />
  <br/><em>Синхронизированная волновая поверхность — эффект Герстнера</em>
</p>

---

### Жизненный цикл сессии

Каждая игровая сессия проходит через **4 фазы**, управляемые сервером (`NetworkSessionController`):

```
  Lobby  ──→  Countdown  ──→  Race  ──→  Results
    ↑                                        │
    └────────── рестарт голосованием ←────────┘
```

**Lobby** — игроки подключаются, выбирают ник, система ожидания (авто-готовность при загрузке).   
**Countdown** — обратный отсчёт 3..2..1 (SyncVar обновляется каждый кадр на сервере).   
**Race** — активная гонка, работают чекпойнты, импульс, урон.   
**Results** — финишная таблица, голосование за рестарт (требуется >50% голосов).

Сервер автоматически реагирует на отключения:
- Мало игроков в Countdown → отмена отсчёта
- Остался один в Race → принудительные Results
- Хост отключился → клиенты возвращаются в меню

---

### Dedicated Server

Выделенный сервер запускается из командной строки без графического интерфейса:

```
./HydroHoverMP_SERVER.x86_64 -dedicatedServer -port 7770 -dataDir ./ServerData
```

- Автоопределение через `#if UNITY_SERVER` или флаг `-dedicatedServer`
- Резолв порта из аргументов `-port` / `-serverPort`
- Сервер не создаёт UI, не рендерит, не инициализирует звук
- Загружает сцену Gameplay как глобальную — один раз
- **Персистентный лидерборд** — пишет рекорды в JSON на диск

---

### Синхронизированная вода

Чтобы физика подушки была честной у всех участников, время волн привязано к **сетевому тику FishNet**:

```csharp
float syncedWaveTime = (float)TimeManager.TicksToTime(
    TimeManager.GetPreciseTick(TickType.Tick));
```

Сервер и каждый клиент читают один и тот же тик → водная поверхность идентична → реакция подушки на волну одинакова. Шейдерные глобалы на Dedicated Server не обновляются — нет графики.

---

### Анти-чит

| Механизм | Описание |
|---|---|
| **Валидация чекпойнтов** | Сервер проверяет порядок прохождения. «Перепрыг» → штраф, прогресс не засчитывается |
| **Серверный кулдаун** | Гидро-импульс — настоящий кулдаун на сервере, локальный — только для UX |
| **Проверка владельца** | Все `[ServerRpc]` проверяют `sender.ClientId == OwnerId` |
| **Валидация фазы** | Действия принимаются только в корректной фазе (Race для чекпойнтов/импульса, Lobby для ready) |
| **Авто-готовность** | Ready устанавливается только при загрузке сцены, не вручную |
| **Откат хоста** | При сбое клиента сервер откатывается — атомарность подключения |

---

### Сетевой лидерборд

Двухуровневая система рекордов:

**Сервер:**
- Хранит `SyncList<NetworkLeaderboardRecord>` — реплицируется всем
- При финише игрока — добавляет запись, сортирует по времени
- Сохраняет в `ServerData/dedicated_leaderboard.json` на диск (Newtonsoft.Json)
- Загружает таблицу при старте сервера

**Клиент:**
- Во время игры читает реплицируемый SyncList
- В меню использует **локальный кэш** (`dedicated_leaderboard_cache.json`)
- Кэш исключает необходимость держать активное сетевое подключение на экране меню

---

## Архитектура проекта

### Слои

```
Assets/Scripts/
├── Core/               # Точка входа, конечный автомат (GameStateMachine)
├── Infrastructure/      # Сервисы, фабрики, провайдеры, Zenject-установщики
│   ├── Services/       # Сеть, сцены, окна, ввод, аудио, игрок, гонка, лидерборд
│   ├── Installers/     # GlobalInstaller, GameplaySceneInstaller
│   ├── Factories/      # StateFactory, UIFactory, IGameObjectFactory
│   └── Providers/      # AssetsAddressablesProvider
├── Features/           # Игровые фичи
│   ├── Networking/     # Сетевые компоненты (PlayerData, Session, HydroPulse, HoverBridge...)
│   ├── Checkpoint/     # Чекпойнты и трек
│   ├── Trigger/        # Триггеры (CheckpointTrigger, WaterZoneTrigger)
│   ├── Camera/         # Камера (DynamicCameraFOV)
│   ├── Audio/          # Аудио (HoverAudioController)
│   └── Effects/        # VFX (HoverVFXController)
├── Physics/            # Физика
│   ├── Hover/          # Подушка, двигатели, аэродинамика
│   └── Water/          # Волны Герстнера, WaterPhysicsSystem, FloatingObject
├── UI/                 # Окна: меню, HUD, пауза, настройки, финиш, лидерборд, загрузка
└── Data/               # Модели данных, пути, константы
```

### DI-контейнеры (Zenject)

- **`GlobalInstaller`** (живёт всё приложение) — синглтоны-сервисы: `GameStateMachine`, `INetworkConnectionService`, `IInputService`, `ISceneLoaderService`, `IWindowService`, `IPlayerService`, `IRaceManagerService`, `IAudioService`, `ILeaderboardService`, фабрики, провайдеры
- **`GameplaySceneInstaller`** (живёт в геймплейной сцене) — сценовые объекты: `WaterPhysicsSystem`, `WaveSettings`, `WindSystem`

### Паттерны

| Паттерн | Где используется |
|---|---|
| **Dependency Injection** | Все сервисы — за интерфейсами |
| **State Machine** | `GameStateMachine` с состояниями `IState` / `IPayloaded<T>` / `IExitable` |
| **Factory** | `StateFactory`, `UIFactory`, `GameObjectFactory` |
| **Provider** | `AssetsAddressablesProvider` (кэш над Addressables) |
| **Observer** | UI подписывается на события сервисов |
| **Singleton** | `NetworkSessionController.Instance`, `NetworkRaceManager.Instance`, `NetworkSpawnPointRegistry.Instance` |

### Сцены

| Сцена | Назначение |
|---|---|
| `Bootstrap` | Входная точка, запуск `GameStateMachine` |
| `MainMenu` | Главное меню (Host/Client), настройки, лидерборд |
| `Gameplay` | Игровой уровень (вода, трасса, чекпойнты) |
| `Level` | Аддитивная геометрия уровня |

---

## Скриншоты

| Скриншот | Описание |
|---|---|
| ![Общий план уровня](presentation/Screenshot_4.png) | **Общий план уровня** — водная трасса с препятствиями |
| ![Волны Герстнера](presentation/Screenshot_1.png) | **Процедурные волны Герстнера** — основа физики подушки |
| ![HUD](presentation/Screenshot_2.png) | **HUD гонки** — таймер, скорость, FPS, чекпойнты, список игроков |
| ![Меню](presentation/Screenshot_3.png) | **Главное меню, настройки, таблица лидеров** |
| ![Два клиента](presentation/Screenshot_5.png) | **Два клиента в одной сессии** — мультиплеерная работа |

---

## Быстрый старт

### Требования

- Unity 6000.3.9f1
- Git LFS
- Windows / Linux

### Установка

```bash
git clone https://github.com/your-org/HydroHoverMP.git
cd HydroHoverMP
```

Откройте `src/HydroHoverMP/` как проект в Unity Hub.

Пакеты установятся автоматически через UPM (`Packages/manifest.json`).

### Запуск локально

**Способ 1: Host + Client (ParrelSync)**
1. Откройте сцену `Bootstrap`
2. Нажмите Play в редакторе → нажмите **Host**
3. Запустите второй экземпляр через ParrelSync → нажмите **Client** → введите `localhost:7770`

**Способ 2: Два редактора**
1. Запустите первый редактор → **Host**
2. Запустите второй редактор → **Client** → `localhost:7770`

### Запуск выделенного сервера

Соберите Server-билд (см. ниже) и запустите:

```bash
./Builds/Server/HydroHoverMP_SERVER.x86_64 \
    -dedicatedServer \
    -port 7770 \
    -dataDir ./ServerData \
    -batchmode \
    -nographics
```

---

## Сборка

### Клиентский билд

```
Build Settings → PC, Mac & Linux Standalone → Build
```

### Server-билд (Linux headless)

```
Build Settings → Dedicated Server (Linux) → Build
```

Или через скрипт:

```bash
# Из папки проекта (не из Unity)
/path/to/Unity -quit -batchmode \
    -buildTarget Linux64 \
    -buildLinux64Player ./Builds/Server/HydroHoverMP_SERVER.x86_64 \
    -dedicatedServer \
    -projectPath ./src/HydroHoverMP
```

---

<p align="center">
  <img src="presentation/Screenshot_3.png" width="80%" alt="Главное меню и лидерборд" style="border-radius: 12px; box-shadow: 0 8px 32px rgba(0,0,0,0.3);" />
  <br/><em>Главное меню, настройки и таблица лидеров</em>
</p>
