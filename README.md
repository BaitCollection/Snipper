# Snipper

A lightweight screenshot tool for Windows. Press **Print Screen**, drag over an area, mark it up, and copy or save it, all in a couple of seconds.

**[Українська версія нижче ↓](#українська)**

- Single C# file, no dependencies and nothing to install: it builds with the compiler that already ships with Windows
- Lives in the system tray and starts with Windows
- Interface in English and Ukrainian

## Features

- **Area selection** across all monitors. Hold **Space** while dragging to move the frame without resizing it. Drag an edge or corner (the blue handles) to adjust a finished selection.
- **Annotation tools:** pen, line, arrow, rectangle, highlighter, text, numbered markers (1, 2, 3…) and blur (pixelate). You can draw past the frame, but only what's inside it ends up in the screenshot.
- **Color and width:** use **−/+**, the mouse wheel, or type a number and press Enter. For blur, the same controls set the strength (1–10). While you're dragging a shape, the mouse wheel changes the width of that shape.
- **Custom hotkey:** any single key (F1, Print Screen…) or a combo with Ctrl/Alt/Shift/Win, including system ones like Win+Shift+S.

## Shortcuts

| Key | Action |
|---|---|
| Print Screen (or left-click the tray icon) | Take a screenshot |
| Ctrl+C | Copy to clipboard |
| Ctrl+S | Save to folder (`Screen_01.png`, `Screen_02.png`… using the first free name) |
| Ctrl+Shift+S | Save as… |
| Ctrl+Z | Undo |
| Esc / right-click | Close |

Screenshots are saved to `Pictures\Screenshots` by default. To change the folder, right-click the tray icon and choose **Choose save folder…**

## Install

1. [Download the code](../../archive/refs/heads/main.zip) and unzip it to a permanent folder, e.g. `C:\Users\<you>\Apps\Snipper`. Avoid `Downloads`, because the app starts with Windows from wherever it is.
2. Double-click **`build.cmd`**. Windows builds `Snipper.exe` with its built-in .NET Framework compiler and starts it. The Snipper icon appears in the tray next to the clock.
3. Autostart with Windows is turned on at first launch. To turn it off, right-click the tray icon and untick **Start with Windows**.

Requires Windows 10 or 11 (.NET Framework 4.x is built in).

## Settings

Right-click the tray icon:

- **Hotkey:** press the combo you want, then click **Save** (or press Enter). The **Print Screen** button restores the default. If you pick a single ordinary key (a letter or digit), it will stop working in other apps, and Snipper warns you about this.
- **Мова / Language:** switch between Ukrainian and English. By default, Snipper follows your Windows display language.

### Print Screen still opens the Windows Snipping Tool?

Snipper intercepts Print Screen itself. If the Snipping Tool still pops up, go to **Settings → Accessibility → Keyboard** and turn off **Use the Print screen key to open screen capture**. Ctrl+Shift+S always works as a fallback.

## Update

Right-click the tray icon and choose **Exit**, replace `Snipper.cs` with the new version, and run `build.cmd` again.

Upgrading from the old **Screenshoter**? Just build and run Snipper. It moves your settings and autostart over, closes the old app, and you can delete the old folder.

## License

[MIT](LICENSE) © 2026 Yehor

---

## Українська

Легкий скріншотер для Windows. Натисніть **Print Screen**, виділіть область, щось на ній позначте й скопіюйте або збережіть. Усе за пару секунд.

- Один файл C#, без залежностей і без встановлення: збирається компілятором, який уже є в Windows
- Живе в треї й запускається разом із Windows
- Інтерфейс українською та англійською

### Можливості

- **Виділення області** на всіх моніторах. Поки тягнете виділення, затисніть **Пробіл**, щоб пересувати рамку, не змінюючи її розміру. Готове виділення можна змінити, потягнувши за край або кут (сині квадратики).
- **Інструменти:** олівець, лінія, стрілка, рамка, маркер, текст, нумерація (1, 2, 3…) і розмиття (пікселізація). Малювати можна й поза рамкою, але в скріншот потрапляє лише те, що всередині.
- **Колір і товщина:** кнопки **−/+**, колесо миші або впишіть число й натисніть Enter. Для розмиття ті самі кнопки змінюють силу (1–10). Поки тягнете фігуру, колесо миші змінює товщину саме цієї фігури.
- **Своя гаряча клавіша:** будь-яка одна клавіша (F1, Print Screen…) або поєднання з Ctrl/Alt/Shift/Win, зокрема системні, як-от Win+Shift+S.

### Клавіші

| Клавіша | Дія |
|---|---|
| Print Screen (або клік по значку в треї) | Зробити скріншот |
| Ctrl+C | Копіювати в буфер обміну |
| Ctrl+S | Зберегти в папку (`Screen_01.png`, `Screen_02.png`…, перша вільна назва) |
| Ctrl+Shift+S | Зберегти як… |
| Ctrl+Z | Скасувати останню дію |
| Esc / права кнопка | Закрити |

Скріншоти за замовчуванням зберігаються в `Зображення\Screenshots`. Щоб змінити папку, клацніть ПКМ по значку й оберіть **Вибрати папку для збереження…**

### Встановлення

1. [Завантажте код](../../archive/refs/heads/main.zip) і розпакуйте в постійну папку, наприклад `C:\Users\<ви>\Apps\Snipper`. Не розпаковуйте в «Завантаження»: програма запускатиметься разом із Windows з того місця, де лежить.
2. Двічі клацніть **`build.cmd`**. Windows збере `Snipper.exe` вбудованим компілятором і запустить програму. У треї біля годинника з'явиться значок Snipper.
3. Автозапуск разом із Windows вмикається при першому запуску. Щоб вимкнути, клацніть ПКМ по значку й зніміть позначку **Запускати разом із Windows**.

Потрібна Windows 10 або 11 (.NET Framework 4.x уже вбудований).

### Налаштування

ПКМ по значку в треї:

- **Гаряча клавіша:** натисніть потрібне поєднання, потім **Зберегти** (або Enter). Кнопка **Print Screen** повертає стандартну. Якщо обрати одну звичайну клавішу (літеру чи цифру), вона перестане працювати в інших програмах, і Snipper про це попередить.
- **Мова / Language:** перемикання між українською та англійською. За замовчуванням Snipper бере мову Windows.

Якщо Print Screen усе одно відкриває «Ножиці» Windows, перейдіть у **Параметри → Спеціальні можливості → Клавіатура** і вимкніть **«Використовувати клавішу Print Screen, щоб відкрити засіб записування екрана»**. Ctrl+Shift+S працює завжди.

### Оновлення

Клацніть ПКМ по значку й оберіть **Вихід**, замініть `Snipper.cs` новою версією і знову запустіть `build.cmd`.

Переходите зі старого **Screenshoter**? Просто зберіть і запустіть Snipper. Він сам перенесе налаштування й автозапуск і закриє стару програму, після чого стару папку можна видалити.
