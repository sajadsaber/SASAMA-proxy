# SASAMA Proxy

A per-application traffic router for Windows: pick which programs (browser,
game, Discord...) route through your local V2Ray SOCKS5 proxy, and leave
everything else on the normal, direct connection.

**I could not compile or run this on a real Windows machine before handing
it to you** - this sandbox only has Linux. Every API call, struct layout,
and byte offset in the WinDivert-facing code was checked line-by-line
against the real, official WinDivert 2.2.2 header and documentation
(included in this zip), not written from memory. That gets the design and
the wiring right with high confidence, but it is not the same as an actual
build+run. Treat the first build in Visual Studio as the real first test.

## 1. What you need to install

- **Visual Studio 2022 Community** (free): https://visualstudio.microsoft.com/
  During install, tick the **".NET desktop development"** workload. That's
  the only workload you need.
- **V2Ray** (or v2rayN, which bundles it) already configured and able to
  connect out from Iran on its own. This app does not replace V2Ray - it
  routes specific programs' traffic *into* V2Ray's local SOCKS5 port.

## 2. Opening and building the project

1. Unzip this folder anywhere (e.g. `Desktop\SasamaProxy`).
2. Double-click `SasamaProxy.sln`. Visual Studio opens.
3. Top toolbar: make sure the configuration dropdown says **x64** (not
   "Any CPU" - this project is deliberately built for x64 only, see
   "Why x64 only" below).
4. Press the green **Run** button (or F5).
5. Windows will show a UAC prompt ("Do you want to allow this app to make
   changes...") - this is expected and required, click **Yes**. The app
   needs administrator rights because WinDivert installs a kernel driver;
   there's no way around this on Windows.
6. The app window should appear.

If step 4 gives build errors, they are almost certainly either (a) the
x64 platform not selected, or (b) "restore NuGet packages" needed - right
click the solution in Solution Explorer → "Restore NuGet Packages" (this
project doesn't actually use any NuGet packages, but VS sometimes wants to
run this step anyway on first open).

## 3. Configuring V2Ray to work with this app

In your V2Ray config, the **inbound** that this app talks to must be a
SOCKS5 inbound with **no authentication**, for example:

```json
{
  "inbounds": [
    {
      "port": 10808,
      "listen": "127.0.0.1",
      "protocol": "socks",
      "settings": {
        "auth": "noauth",
        "udp": false
      }
    }
  ]
}
```

The IP/port here must match what you type into the app's proxy info row
(defaults: `127.0.0.1` / `10808`). If you're using v2rayN, this is
usually already its default local SOCKS listener - check v2rayN's
settings to confirm the port.

## 4. Testing it - do this before trusting it with a game

1. Start V2Ray/v2rayN first, confirm it can browse the internet on its own.
2. Launch SASAMA Proxy (as admin, via Visual Studio's Run for now).
3. Click **+ Add**, pick a running browser (or type e.g. `chrome.exe`),
   make sure its checkbox is on.
4. Click **Ping** in the proxy info row - it should show a latency number,
   not "Time Out". If it says Time Out, V2Ray isn't reachable at the
   IP/port you configured - fix that first before going further.
5. Click **START PROXY**. The header should switch to "Connected" in green.
6. Close and reopen the browser you added (a NEW connection is what gets
   picked up - tabs/connections already open before you pressed Start
   won't retroactively get redirected). Visit a site that only works
   through your proxy. If it loads, the whole pipeline works end to end.
7. Check the **Log** panel - you should see lines like "Tracking new
   connection: chrome.exe...". If you never see this for the app you
   added, the Flow-layer tracking isn't matching (check the exact process
   name - some apps have a different image name than their shortcut name).

Only after this works with a browser would I trust it with a game.

## 5. What actually works right now, and what doesn't

**Works:** TCP traffic from any process you add to the list - browsers,
Discord's own client traffic and text chat, Steam's background traffic,
game launchers/lobbies/matchmaking that use TCP, downloads, basically
anything over HTTP/HTTPS or a plain TCP socket.

**Does NOT work yet - UDP.** This is the single biggest limitation and
you need to know about it going in: **actual in-game gameplay traffic for
most multiplayer games runs over UDP**, and this build does not redirect
UDP at all - it passes it straight through, unproxied. I made a deliberate
call here rather than ship something I couldn't test: SOCKS5 UDP
(UDP ASSOCIATE + per-packet header wrapping, keeping a control connection
alive, re-matching reply packets) is a second, genuinely harder engineering
problem than TCP NAT, with a lot more ways for untested code to fail
silently. Rather than hand you a UDP implementation I have zero confidence
in, I'm telling you plainly: **this version proxies TCP only.** Once you've
confirmed TCP works reliably in real use, say so and we'll do UDP as its
own follow-up, building on a foundation you've actually seen work - not
stacking more unverified code on top of something neither of us has run
yet.

**Does NOT get around DNS blocking.** This app only sees the destination
*IP address* a program is already connecting to - by the time a packet
reaches WinDivert, DNS resolution already happened. If Iran's DNS
interference gives an app the wrong IP for a domain, this app faithfully
proxies a connection to that wrong IP - it can't fix a bad DNS answer after
the fact. Browsers with "Secure DNS" / DNS-over-HTTPS turned on in their
own settings are unaffected by this; other apps may be.

**IPv6** packets are passed through untouched (not proxied, not broken -
just ignored). Nearly everything in Iran's residential/mobile network
stack is IPv4-first anyway, so this is a minor gap in practice.

**"Ping" measures something slightly different than the spec asked for.**
There's no such thing as an ICMP ping through a SOCKS5 proxy - SOCKS5 only
carries TCP (and, if enabled, UDP), never ICMP. What the Ping button and
the automatic 5-second refresh actually measure is the time to open a
real proxied TCP connection to 1.1.1.1:443 - a truer "will my traffic
actually get through" number than a raw ping would have been anyway.

## 6. Known rough edges (not bugs - inherent to this technique)

- **The very first connection of a session can occasionally slip through
  unproxied once**, then self-correct. This app uses two separate
  WinDivert taps (one to learn which process owns a connection via
  `SOCKET_CONNECT`, one to actually redirect packets) that run as
  independent threads; on rare occasions the very first SYN packet of a
  brand new connection can win a race against the thread that hasn't yet
  recorded which process it belongs to. TCP automatically retransmits an
  unanswered SYN after about a second, and by then the redirect is in
  place - so worst case is a ~1 second delay on some new connections, not
  a broken connection.
- If the app **crashes** (it shouldn't, but nothing is bulletproof), a
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
  **If Windows Defender deletes the zip on download:** this is a known,
  expected false positive for WinDivert-based tools, not a sign anything
  was tampered with. Rather than disabling Defender entirely (which leaves
  you unprotected against everything, not just this), add a one-time
  exclusion instead: Windows Security → Virus & threat protection → Manage
  settings → Add or remove exclusions → Add an exclusion → Folder, and
  point it at wherever you extract this project. Then turn Defender back
  on generally.

## 7. "Run at Windows startup" - one deliberate change from the spec

You asked for a simple on/off toggle for "run at Windows startup," and
that toggle is there. But under the hood I did **not** implement it as
the usual registry Run-key trick, on purpose: this app needs administrator
rights (for WinDivert), and Windows does not cleanly auto-launch an
admin-required app from the Run key - it either silently fails to start,
or nags with a UAC prompt on every single boot, which is exactly the kind
of confusing thing your non-technical users would run into and blame the
app for. Instead, the toggle registers a **Task Scheduler task set to run
with highest privileges at logon** - the standard, correct way to
auto-start something that needs elevation without a UAC prompt every time.
Functionally it's still just an on/off switch to the user; nothing to do
here.

## 8. Why x64 only

The project targets x64 specifically rather than "Any CPU," to match the
`WinDivert.dll` / `WinDivert64.sys` binaries bundled in `Redist\x64` -
P/Invoke requires the managed process and the native DLL to be the same
bitness. Nearly every Windows 10/11 PC (yours included, from what you've
told me) is x64, so this isn't a real-world limitation - just noting it in
case you ever need 32-bit support later, which would mean adding the
32-bit WinDivert binaries and a second build configuration.

## 9. Distributing this to your users

- Build in **Release** mode (dropdown next to the x64 selector), then zip
  up everything in `SasamaProxy\SasamaProxy\bin\Release` - that folder is
  the standalone, extract-and-run package. `WinDivert.dll` and
  `WinDivert64.sys` are copied there automatically at build time.
- `LICENSE-WinDivert.txt` (in this project's root) is WinDivert's LGPLv3
  license - keep it alongside the app when you distribute it, since you're
  redistributing its binaries unmodified. Your own C# code is entirely
  yours to keep closed-source; nothing about WinDivert's license requires
  you to open-source your app.
- First launch on a fresh machine is when the WinDivert kernel driver
  actually installs - that also requires admin, which your manifest
  already forces.

## 10. File map, if you ever want to find something

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
