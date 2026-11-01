# Isolated native hover lab

This experiment investigates the application's own hover appearance while the real pointer stays elsewhere. It is a separate executable and changes no desktop-pet behavior. No messages can be sent to another application's controls: each target must be one of the two exact owned controls, a live child of this lab, and in this process.

Build in Windows PowerShell:

```powershell
./experiments/native-hover/build.ps1
./dist/native-hover-lab/Halloweb.NativeHoverLab.exe
```

The spider visits a standard Windows button and then a native flat toolbar. **Replay experiment** repeats the sequence. The toolbar's highlight stays visible after the interactive run so it can be inspected. Closing the lab clears that state and its spider.

For an automatically closing verification run, keep the real pointer outside the lab's controls:

```powershell
./dist/native-hover-lab/Halloweb.NativeHoverLab.exe --verify
```

Results are saved beside this separate executable as `native-hover-lab-results.txt` and `.json`. They contain the measured native hot states, event counters, and pointer/foreground stability flags.

## Observed result — 1 November 2026

| Test | Result on the tested desktop |
| --- | --- |
| One `WM_MOUSEMOVE` to an owned `FlatStyle.System` button | `BST_HOT` true immediately, false after 25, 100, and 300 ms. |
| Normal button leave tracking | Windows delivered leave messages; the experiment did not suppress them or repeatedly send moves to manufacture persistence. |
| `TB_SETHOTITEM` on an owned native flat toolbar | Item 0 stayed hot for at least 500 ms; setting -1 cleared it. |
| Button/toolbar clicks and mouse-button down/up | Zero. |
| Real cursor and foreground window across sampled ticks | Unchanged. |

The native toolbar result proves a control-specific native highlight. It does not prove that arbitrary buttons in other apps support a second, non-clicking pointer. Toolbar hot-item changes may notify the owning app; some frameworks even support activation on hover. This lab has no file-opening or other action attached to either target.

References: [button state](https://learn.microsoft.com/en-us/windows/win32/controls/bm-getstate), [mouse tracking](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-trackmouseevent), [toolbar hot item](https://learn.microsoft.com/en-us/windows/win32/controls/tb-sethotitem), [WPF hover activation](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.clickmode).
