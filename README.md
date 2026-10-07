## SASAMA Proxy

<img width="656" height="1116" alt="Screenshot 2026-10-07 022155" src="https://github.com/user-attachments/assets/69fc5de6-9aaf-4f0d-8429-648ee43d9c01" />

## SASAMA Proxy

درود به همه امیدوارم حالتون خوب باشه 

من خیلی آدم خنگی هستم وبه کمک شما باهوشا نیاز دارم

قبل از هر چیزی لطفا توی این دیسکورد که مخصوص همین پروژه ساختم جوین شین که به صورت گروهی روش کار کنیم 
البته این بهونس من فقط خیلی تنهام و دنبال دوست میگردم پس لطفا باهام دوست بشین ترو خدااااا
https://discord.gg/ncaaXkkZt

قبل از هرچی زبون این نرم افزار c# هست و از windivert استفاده میکنه
این پروژه قراره یه پروکسی لوکال باشه که باهاش  خیلی راحت و بدون دردسر بتونی اسپلیت تانل کنی توی ویندوز که بعضی از نرم افزار ها از فیلترشکن استفاده کنن و بقیه نرم افزار ها از ترافیک عادی استفاده کنن
مثل پروکسیفایر ولی با کاربرد راحت تر و یو آی فرندلی تر و مهم تر از همه قراره برای همیشه رایگان باشه که هموطنامون که زیاد سر از کامپیوتر در نمیارن بتونن راحت استفاده کنن به راحتی اندروید بدون نیاز به رول نوشتن یا اسم پروسس نرم افزار هارو پیدا کردن

خلاصه داستان اینه که من هیچی از برنامه نویسی بلد نبودم فقط میخواستم هرطور شده اینو بسازم.
پس ویژوال استودیو رو نصب کردم و تحقیق کردم بهترین راهش چیه چه زبونی باید باشه و….
با کمک یوتوب یه ماه وقت گذاشتم با تماشای کلی ویدیو قدم به قدم نوشتمش بدون اینکه خودم بفهمم چیکار میکنم

خلاصه به جایی رسید که با بدبختی کار میکرد و مغز معیوب من نمیتونست مشکلات و باگ هارو فیکس کنه
بخاطر همین یه اشتباه بزرگ مرتکب شدم بدون اینکه بک آپ بگیرم دادمش دست کلاد گفتم عیب هاشو برطرف کنه اونم بدتر نابودش کرد 
الان من موندم با این بیس کد که خودمم هیچی ازش نمیفهمم

چون گاااااااوم

لطفا کمکم کنید درستش کنم

از درایور های windivert استفاده میکنه چون میخواستم کرنال لول باشه که نیاز نباشه حتما قبل از نرم افزار لانچ بشه و زیاد به باگ نخوره

کارایی که الان میکنه اینه:

۱. کاملا وصل میشه به ای پی لوکال و پورتی که بهش بدین مثلا 10808 برای v2ray و ازش پینگ میگیره و نشون میده که متصله

۲. به راحتی تمام نرم افزار هایی که تو بک گراند باز هستن رو شناسایی می‌کنه و خیلی راحت توی لیست instance ها ادشون می‌کنه (یعنی دیگه نیاز نیست با هزار بدبختی اسم نرم افزار رو پیدا و اد کنی)

۳. یه پنجره لاگ براش اضافه کردم که کاملا نشون میده ترافیک مثلا opera.exe شناسایی و روت میشه و یسری اطلاعات دیگه (ولی نمیدونم چرا همچنان سایت ایرانی باز میکنه و سایت فیلتر نه)

۴. همه چیزش ایندکیتور داره که کاربر عادی بتونه راحت استفاده کنه مثلا اگه نتونه وصل بشه پورت یا آی پی اشتباه باشه یا هر چیزی تو این مایه ها مینویسه disconnected و پینگ رو نشون نمیده بجاش تایم اوت میده 

ضمنن توجه داشته باشید دفعه اول توی system tray باز میشه پس اگه بازش کردین دیدین چیزی نیورد از تو تری بازش کنیم چون lunch In background دیفالت روشنه و واقعا زیادی گشادم که خاموشش کنم و دوباره آپلود کنم تو ریپازتوری

خلاصه‌ که اگه سوال دیگه ای هم داشتین میتونین تو اینستا یا دیسکورد بپرسین 

ترو خدا باهام دوست بشین و کمکم کنید

دیسکورد:   https://discord.gg/ncaaXkkZt

اینستاگرام: https://Instagram.com/sajadsaberm

یوتوب: https://www.youtube.com/@sajadsaber

اینم یسری اطلاعات که به در شما باهوشا میخوره که با کمک کلاد سعی کردم بگم چیکار کردم


A per-application traffic router for Windows: pick which programs (browser,
game, Discord...) route through your local V2Ray SOCKS5 proxy, and leave
everything else on the normal, direct connection.

## . What you need to install

- **Visual Studio 2022 Community**: https://visualstudio.microsoft.com/
  During install, tick the **".NET desktop development"** workload. That's
  the only workload you need.
- **V2Ray** (or v2rayN, which bundles it) already configured and able to
  connect out from Iran on its own. This app does not replace V2Ray - it
  routes specific programs' traffic *into* V2Ray's local SOCKS5 port.



The IP/port here must match what you type into the app's proxy info row
(defaults: `127.0.0.1` / `10808`). If you're using v2rayN, this is
usually already its default local SOCKS listener - check v2rayN's
settings to confirm the port.

## . Testing it

1. Start V2Ray/v2rayN first, confirm it can browse the internet on its own.
2. Launch SASAMA Proxy (as admin, via Visual Studio's Run for now).
3. Click **+ Add**, pick a running browser (or type e.g. `chrome.exe`),
   make sure its checkbox is on.
4. Click **Ping** in the proxy info row - it should show a latency number,
   not "Time Out". If it says Time Out, V2Ray isn't reachable at the
   IP/port you configured - fix that first before going further.
5. Click **START PROXY**. The header should switch to "Connected" in green.


- If the app **crashes** a
  global crash handler force-closes the WinDivert driver before the
  process dies, so a crash cannot leave your internet stuck through a dead
  driver. Restart the app afterward and check the Log panel / the log file
  at `%AppData%\SasamaProxy\sasama.log` for what happened.
- **Antivirus / anti-cheat software** sometimes flags WinDivert's driver,
  since "a program that reroutes network traffic" is exactly what a lot of
  malware also does. WinDivert itself is legitimate, widely-used, and the
  copy in this zip is the untouched official release - but you may need to
  allowlist it, and some strict kernel-level anti-cheats may refuse to run
  alongside any traffic-filtering driver at all. Test with whichever game
  matters to you early.



## . Why x64 only

The project targets x64 specifically rather than "Any CPU," to match the
`WinDivert.dll` / `WinDivert64.sys` binaries bundled in `Redist\x64` -
P/Invoke requires the managed process and the native DLL to be the same
bitness. Nearly every Windows 10/11 PC (yours included, from what you've
told me) is x64, so this isn't a real-world limitation - just noting it in
case you ever need 32-bit support later, which would mean adding the
32-bit WinDivert binaries and a second build configuration.




## . File map, if you ever want to find something

```
Native/WinDivertInterop.cs   Raw P/Invoke into WinDivert.dll
Core/NatEngine.cs            The actual packet redirection (the core of the app)
Core/TcpRelay.cs             Speaks SOCKS5 to V2Ray on behalf of redirected connections
Core/Socks5Client.cs         Minimal SOCKS5 handshake, used by TcpRelay and the latency check
Core/ProxyMonitor.cs         Online/offline + latency
Core/StartupManager.cs       The Task Scheduler logic from section 7
Core/TrayIconManager.cs      System tray icon
Core/ConfigStore.cs          Loads/saves settings + your program list
Core/Logger.cs               Powers the Log panel + the log file on disk
MainWindow.xaml(.cs)         Main window
SettingsWindow.xaml(.cs)     Gear-icon settings window
AddProcessWindow.xaml(.cs)   The "+ Add" program picker
App.xaml                     All colors and button styles live here - start here to restyle
```
