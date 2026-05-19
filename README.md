# 🎵 MusicWidget - VibeCoding Edition (v1.5)

A sleek, lightweight, and transparent media control widget for Windows that dynamically shifts its color palette based on the "acoustic signature" of the playing track. Built entirely with native Win32 API integrations and WPF, rejecting bloated third-party tools to deliver high performance, absolute stability, and instant privacy control.

## ✨ Key Features & Architecture

* **Global Invisibility Cloak (`Ctrl+Shift+M`):** Boss coming? In the middle of an intense game? Press `Ctrl+Shift+M` from anywhere inside the operating system to instantly hide or reveal the widget. Powered by low-level Windows HotKey hooks, this action is instantaneous, lightweight, and focus-immune.
* **Smart Auto-Docking (Taskbar Magnetism):** Drag the widget anywhere on your desktop freely. The moment you release it over the Windows Taskbar (`Shell_TrayWnd`), the widget calculates the layout boundaries and instantly snaps/locks itself seamlessly into the taskbar space.
* **Win32 Taskbar Ownership (No More Jumping Layouts):** Traditional `SetParent` methods are completely avoided as they break the internal WPF layout engine and cause erratic window jumping. Instead, the widget maintains a custom Win32 ownership hierarchy (`GWL_HWNDPARENT`), binding itself as a native extension of the taskbar. It stays locked vertically without a single pixel of stutter or displacement when toggled with the hotkey.
* **Anti-Win+D Protection (Stubborn Display):** Standard windows get minimized when hitting `Win+D` (Show Desktop). Because this widget is legally "owned" by the Windows Taskbar—which never minimizes—the widget safely ignores the OS-wide hide command and remains pinned to the screen at all times.
* **Focus-Immune Interface (`WS_EX_NOACTIVATE`):** Clicking on the widget, skipping tracks, or interacting with the context menu will **never** steal focus from your active windows, code editors, or full-screen video games.
* **Acoustic Signature (Hash Coloring):** Every song title and artist combination goes through a deterministic `GetHashCode` algorithm, instantly generating a distinct, beautiful HSL color spectrum for the audio visualizer bars.
* **Native Multi-Language Support:** The system automatically adapts its interface based on local context, natively switching between English and Turkish placeholders (`Bilinmiyor`, `Şarkı bekleniyor...`) without any external translation package dependencies.
* **State Persistence:** The widget remembers its environment. Even if you close it while docked, it reads the local state configuration (`dock.txt` / `pos.txt`) upon reboot and seamlessly respawns exactly where you left it.

## 🛠️ Technical Breakdown
* **Language/Framework:** C# / .NET (Fully guarded with Nullable Reference Types)
* **UI Engine:** WPF (Windows Presentation Foundation) with transparent border styling
* **Audio Analysis:** Wasapi Loopback Capture powered by `NAudio`
* **Media Control:** Windows System Media Transport Controls (SMTC) Integration
* **Low-Level Hooks:** Native User32 bindings (`SetWindowPos`, `FindWindow`, `GetWindowLongPtr`, `RegisterHotKey`, `UnregisterHotKey`)

## 🚀 Getting Started & Build
1. Clone the repository.
2. Run `dotnet publish -c Release -r win-x64 --self-contained` to compile your standalone standalone `.exe`.
3. Launch, drag onto your taskbar to lock, and manage your media with global hotkeys!

> "This project was built under the 'VibeCoding' philosophy—writing pure, system-level code tailored directly to local needs instead of stacking fragile and bloated third-party extensions."
