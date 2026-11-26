# Snipper

A lightweight screenshot tool for Windows. Press **Print Screen**, drag over an area, mark it up, and copy or save it, all in a couple of seconds.

**[Українська версія нижче ↓](#українська)**

- Single C# file, no dependencies and nothing to install: it builds with the compiler that already ships with Windows
- Lives in the system tray and starts with Windows
- Interface in English and Ukrainian

## Features

- **Area selection** across all monitors. Hold **Space** while dragging to move the frame without resizing it. Drag an edge or corner (the blue handles) to adjust a finished selection.
- **Annotation tools:** pen, line, arrow, rectangle, highlighter, text, numbered markers (1, 2, 3…) and blur (pixelate). You can draw past the frame, but only what's inside it ends up in the screenshot. Hold **Shift** with the highlighter for a straight line. The **Aa** button picks the text font (name, bold, italic).
- **Pin:** turn the capture into a sticky note that stays on top of all windows. Drag it around, scroll to zoom, Ctrl+scroll to change opacity, double-click or Esc to close, right-click for a menu (copy, save…).
- **Timer:** pick 3, 5 or 10 seconds. Snipper hides and then captures the same area again, so you have time to open a menu or position the cursor.
- **Text recognition (OCR):** finds the text in the selection, fully offline, using the OCR built into Windows 10/11. Select words with the mouse like in a PDF (double-click selects a line, Shift+click extends, Ctrl+A selects all), then Ctrl+C to copy. Ctrl+T copies all the text at once. It recognizes the languages that have a language pack installed in Windows.
- **Color picker:** a magnifier with the color code follows the cursor. Click to copy HEX (`#RRGGBB`), Shift+click to copy RGB. The picked color becomes the drawing color.
- **QR codes:** if the selection contains a QR code, a bar with its contents appears under the frame. For a link, it shows the domain in large type so you can see where it leads; click it or **Open** to open it in the browser. For anything else (text, Wi-Fi, contact…), use **Copy**. Links never open on their own.
- **Repeat last area** (↻ or Ctrl+R): puts the frame exactly where your last saved, copied or pinned screenshot was, with a fresh image inside. Handy for grabbing video frames from the same spot. Also available from the tray menu.
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
| Ctrl+R | Repeat last area |
| Esc / right-click | Close |

Screenshots are saved to `Pictures\Screenshots` by default. To change the folder, right-click the tray icon and choose **Choose save folder…**

## Install

1. [Download the code](../../archive/refs/heads/main.zip) and unzip it to a permanent folder, e.g. `C:\Users\<you>\Apps\Snipper`. Avoid `Downloads`, because the app starts with Windows from wherever it is.
2. Double-click **`build.cmd`**. Windows builds `Snipper.exe` with its built-in .NET Framework compiler and starts it. The Snipper icon appears in the tray next to the clock.
3. Autostart with Windows is turned on at first launch. To turn it off, right-click the tray icon and untick **Start with Windows**.

Requires Windows 10 or 11 (.NET Framework 4.x is built in). If text recognition can't be built on your PC, `build.cmd` builds Snipper without it and saves the details to `build_ocr.log`.

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
- **Інструменти:** олівець, лінія, стрілка, рамка, маркер, текст, нумерація (1, 2, 3…) і розмиття (пікселізація). Малювати можна й поза рамкою, але в скріншот потрапляє лише те, що всередині. Маркер із затиснутим **Shift** малює рівну лінію. Кнопка **Aa** обирає шрифт для тексту (назва, жирний, курсив).
- **Закріпити:** знімок стає стікером поверх усіх вікон. Його можна тягати мишею, колесо змінює масштаб, Ctrl+колесо — прозорість, подвійний клік або Esc закриває, ПКМ відкриває меню (копіювати, зберегти…).
- **Таймер:** 3, 5 або 10 секунд. Snipper ховається, а потім знімає ту саму зону ще раз, щоб ви встигли відкрити меню чи навести курсор.
- **Розпізнавання тексту (OCR):** знаходить текст у виділенні, повністю офлайн, вбудованим у Windows 10/11 розпізнаванням. Виділяйте слова мишею, як у PDF (подвійний клік — рядок, Shift+клік — розширити, Ctrl+A — усе), і копіюйте Ctrl+C. Ctrl+T одразу копіює весь текст. Розпізнає мови, для яких у Windows встановлено мовний пакет.
- **Піпетка:** біля курсора лупа з кодом кольору. Клік копіює HEX (`#RRGGBB`), Shift+клік — RGB. Вибраний колір одразу стає кольором для малювання.
- **QR-коди:** якщо у виділенні є QR-код, під рамкою з'являється плашка з його вмістом. Для посилання великим шрифтом видно домен, тобто куди веде код; клік по ньому або **Відкрити** відкриває його в браузері. Для іншого вмісту (текст, Wi-Fi, контакт…) є кнопка **Копіювати**. Посилання ніколи не відкриваються самі.
- **Повторити попередню зону** (↻ або Ctrl+R): рамка стає точно там, де був останній збережений, скопійований чи закріплений скріншот, а зображення в ній свіже. Зручно знімати кадри відео з тієї самої області. Також є в меню трею.
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
| Ctrl+R | Повторити попередню зону |
| Esc / права кнопка | Закрити |

Скріншоти за замовчуванням зберігаються в `Зображення\Screenshots`. Щоб змінити папку, клацніть ПКМ по значку й оберіть **Вибрати папку для збереження…**

### Встановлення

1. [Завантажте код](../../archive/refs/heads/main.zip) і розпакуйте в постійну папку, наприклад `C:\Users\<ви>\Apps\Snipper`. Не розпаковуйте в «Завантаження»: програма запускатиметься разом із Windows з того місця, де лежить.
2. Двічі клацніть **`build.cmd`**. Windows збере `Snipper.exe` вбудованим компілятором і запустить програму. У треї біля годинника з'явиться значок Snipper.
3. Автозапуск разом із Windows вмикається при першому запуску. Щоб вимкнути, клацніть ПКМ по значку й зніміть позначку **Запускати разом із Windows**.

Потрібна Windows 10 або 11 (.NET Framework 4.x уже вбудований). Якщо розпізнавання тексту не вдасться зібрати на вашому ПК, `build.cmd` збере Snipper без нього, а подробиці збереже в `build_ocr.log`.

### Налаштування

ПКМ по значку в треї:

- **Гаряча клавіша:** натисніть потрібне поєднання, потім **Зберегти** (або Enter). Кнопка **Print Screen** повертає стандартну. Якщо обрати одну звичайну клавішу (літеру чи цифру), вона перестане працювати в інших програмах, і Snipper про це попередить.
- **Мова / Language:** перемикання між українською та англійською. За замовчуванням Snipper бере мову Windows.

Якщо Print Screen усе одно відкриває «Ножиці» Windows, перейдіть у **Параметри → Спеціальні можливості → Клавіатура** і вимкніть **«Використовувати клавішу Print Screen, щоб відкрити засіб записування екрана»**. Ctrl+Shift+S працює завжди.

### Оновлення

Клацніть ПКМ по значку й оберіть **Вихід**, замініть `Snipper.cs` новою версією і знову запустіть `build.cmd`.

Переходите зі старого **Screenshoter**? Просто зберіть і запустіть Snipper. Він сам перенесе налаштування й автозапуск і закриє стару програму, після чого стару папку можна видалити.
