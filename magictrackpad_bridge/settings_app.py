from __future__ import annotations

from dataclasses import asdict, replace
import json
import os
from pathlib import Path
import subprocess
import sys
import tkinter as tk
from tkinter import filedialog, messagebox, ttk

from .config import AppConfig, HotkeyConfig, load_config
from .hotkeys import normalize_hotkey, parse_hotkey


class SettingsApp:
    def __init__(self, root: tk.Tk, config_path: Path) -> None:
        self.root = root
        self.config_path = config_path
        self.config = load_config(config_path)
        self.vars: dict[str, tk.Variable] = {}
        self.hotkey_vars: dict[str, tk.StringVar] = {}
        self.bridge_process: subprocess.Popen | None = None

        root.title("Magic Trackpad Bridge Settings")
        root.geometry("760x640")
        root.minsize(680, 560)

        self._build()
        self._load_values()

    def _build(self) -> None:
        container = ttk.Frame(self.root, padding=14)
        container.pack(fill=tk.BOTH, expand=True)

        header = ttk.Frame(container)
        header.pack(fill=tk.X)
        ttk.Label(header, text="Magic Trackpad Bridge", font=("Segoe UI", 16, "bold")).pack(anchor=tk.W)
        ttk.Label(header, text="Bluetooth gestures, pointer behavior, scrolling, and keybinds").pack(anchor=tk.W)

        notebook = ttk.Notebook(container)
        notebook.pack(fill=tk.BOTH, expand=True, pady=(14, 12))

        pointer = self._tab(notebook, "Pointer")
        scroll = self._tab(notebook, "Scroll")
        clicks = self._tab(notebook, "Clicks")
        gestures = self._tab(notebook, "Gestures")
        service = self._tab(notebook, "Service")

        self._build_pointer_tab(pointer)
        self._build_scroll_tab(scroll)
        self._build_clicks_tab(clicks)
        self._build_gestures_tab(gestures)
        self._build_service_tab(service)

        actions = ttk.Frame(container)
        actions.pack(fill=tk.X)
        ttk.Button(actions, text="Save", command=self.save).pack(side=tk.RIGHT)
        ttk.Button(actions, text="Save and Run Bridge", command=self.save_and_run).pack(side=tk.RIGHT, padx=(0, 8))
        ttk.Button(actions, text="Reset to Defaults", command=self.reset_defaults).pack(side=tk.LEFT)

    def _tab(self, notebook: ttk.Notebook, title: str) -> ttk.Frame:
        frame = ttk.Frame(notebook, padding=14)
        notebook.add(frame, text=title)
        return frame

    def _build_pointer_tab(self, parent: ttk.Frame) -> None:
        self._check(parent, "pointer_enabled", "Enable one-finger pointer movement")
        self._scale(parent, "pointer_sensitivity", "Pointer sensitivity", 0.05, 1.0)
        self._check(parent, "invert_pointer_x", "Reverse horizontal pointer direction")
        self._check(parent, "invert_pointer_y", "Reverse vertical pointer direction")

    def _build_scroll_tab(self, parent: ttk.Frame) -> None:
        self._check(parent, "scroll_enabled", "Enable two-finger scrolling")
        self._check(parent, "natural_scroll", "Natural scroll direction")
        self._scale(parent, "scroll_sensitivity", "Scroll sensitivity", 0.05, 2.0)
        self._check(parent, "horizontal_scroll_enabled", "Enable horizontal scrolling")

    def _build_clicks_tab(self, parent: ttk.Frame) -> None:
        self._check(parent, "tap_to_click", "Tap to click")
        self._choice(parent, "one_finger_tap_button", "One-finger tap action", ["left", "right", "middle", "none"])
        self._choice(parent, "two_finger_tap_button", "Two-finger tap action", ["right", "left", "middle", "none"])
        self._choice(parent, "three_finger_tap_button", "Three-finger tap action", ["middle", "left", "right", "none"])
        self._scale(parent, "tap_max_seconds", "Tap max time", 0.05, 0.50)
        self._scale(parent, "tap_max_distance", "Tap movement tolerance", 10.0, 250.0)
        self._check(parent, "secondary_click_enabled", "Two-finger secondary click")
        self._check(parent, "three_finger_middle_click", "Three-finger middle click")
        self._choice(parent, "physical_click_button", "Physical click action", ["left", "right", "middle", "none"])
        self._choice(parent, "multi_finger_physical_click_button", "Multi-finger physical click action", ["right", "left", "middle", "none"])

    def _build_gestures_tab(self, parent: ttk.Frame) -> None:
        self._check(parent, "pinch_zoom_enabled", "Pinch to zoom")
        self._choice(parent, "pinch_zoom_modifier", "Pinch zoom modifier", ["Ctrl", "Alt", "Shift", "Win", "none"])
        self._scale(parent, "pinch_sensitivity", "Pinch sensitivity", 0.10, 2.0)
        self._scale(parent, "pinch_threshold", "Pinch activation threshold", 2.0, 80.0)
        self._check(parent, "three_finger_swipes_enabled", "Enable three- and four-finger swipe keybinds")
        self._scale(parent, "swipe_threshold", "Horizontal swipe distance", 100.0, 1400.0)
        self._scale(parent, "swipe_vertical_threshold", "Vertical swipe distance", 100.0, 1400.0)

        ttk.Separator(parent).pack(fill=tk.X, pady=12)
        ttk.Label(parent, text="Swipe keybinds", font=("Segoe UI", 11, "bold")).pack(anchor=tk.W, pady=(0, 8))
        grid = ttk.Frame(parent)
        grid.pack(fill=tk.X)
        labels = [
            ("three_finger_swipe_left", "3 fingers left"),
            ("three_finger_swipe_right", "3 fingers right"),
            ("three_finger_swipe_up", "3 fingers up"),
            ("three_finger_swipe_down", "3 fingers down"),
            ("four_finger_swipe_left", "4 fingers left"),
            ("four_finger_swipe_right", "4 fingers right"),
            ("four_finger_swipe_up", "4 fingers up"),
            ("four_finger_swipe_down", "4 fingers down"),
        ]
        for row, (name, label) in enumerate(labels):
            ttk.Label(grid, text=label).grid(row=row, column=0, sticky=tk.W, pady=4)
            var = tk.StringVar()
            self.hotkey_vars[name] = var
            entry = ttk.Entry(grid, textvariable=var, width=24)
            entry.grid(row=row, column=1, sticky=tk.EW, padx=(12, 6), pady=4)
            ttk.Button(grid, text="Record", command=lambda target=entry: self.record_hotkey(target)).grid(row=row, column=2, pady=4)
        grid.columnconfigure(1, weight=1)

    def _build_service_tab(self, parent: ttk.Frame) -> None:
        self._check(parent, "enable_multitouch_on_start", "Enable multitouch automatically")
        self._scale(parent, "reenable_interval_seconds", "Bluetooth reconnect refresh interval", 3.0, 120.0)
        self._check(parent, "log_raw_reports", "Log raw HID reports for diagnostics")

        row = ttk.Frame(parent)
        row.pack(fill=tk.X, pady=8)
        ttk.Label(row, text="Raw log path").pack(side=tk.LEFT)
        var = tk.StringVar()
        self.vars["raw_log_path"] = var
        ttk.Entry(row, textvariable=var).pack(side=tk.LEFT, fill=tk.X, expand=True, padx=12)
        ttk.Button(row, text="Browse", command=self.pick_log_file).pack(side=tk.RIGHT)

        ttk.Separator(parent).pack(fill=tk.X, pady=14)
        ttk.Button(parent, text="Run Bridge Now", command=self.run_bridge).pack(anchor=tk.W, pady=4)
        ttk.Button(parent, text="Run 5 Second Dry Test", command=self.run_dry_test).pack(anchor=tk.W, pady=4)
        ttk.Button(parent, text="Open Config Folder", command=self.open_config_folder).pack(anchor=tk.W, pady=4)

    def _check(self, parent: ttk.Frame, name: str, label: str) -> None:
        var = tk.BooleanVar()
        self.vars[name] = var
        ttk.Checkbutton(parent, text=label, variable=var).pack(anchor=tk.W, pady=5)

    def _scale(self, parent: ttk.Frame, name: str, label: str, minimum: float, maximum: float) -> None:
        frame = ttk.Frame(parent)
        frame.pack(fill=tk.X, pady=6)
        ttk.Label(frame, text=label).pack(anchor=tk.W)
        var = tk.DoubleVar()
        self.vars[name] = var
        value_label = ttk.Label(frame, width=8)
        value_label.pack(side=tk.RIGHT)

        def update_value(*_: object) -> None:
            value_label.configure(text=f"{var.get():.2f}")

        slider = ttk.Scale(frame, from_=minimum, to=maximum, orient=tk.HORIZONTAL, variable=var, command=lambda _value: update_value())
        slider.pack(side=tk.LEFT, fill=tk.X, expand=True, pady=(4, 0))
        var.trace_add("write", update_value)

    def _choice(self, parent: ttk.Frame, name: str, label: str, choices: list[str]) -> None:
        frame = ttk.Frame(parent)
        frame.pack(fill=tk.X, pady=6)
        ttk.Label(frame, text=label).pack(side=tk.LEFT)
        var = tk.StringVar()
        self.vars[name] = var
        combo = ttk.Combobox(frame, textvariable=var, values=choices, state="readonly", width=16)
        combo.pack(side=tk.RIGHT)

    def _load_values(self) -> None:
        data = asdict(self.config)
        gestures = data["gestures"]
        for name, var in self.vars.items():
            if name in gestures:
                var.set(gestures[name])
            elif name in data:
                var.set(data[name])
        for name, var in self.hotkey_vars.items():
            var.set(gestures["hotkeys"][name])

    def save(self) -> bool:
        try:
            config = self._config_from_form()
        except ValueError as exc:
            messagebox.showerror("Invalid setting", str(exc))
            return False

        self.config_path.parent.mkdir(parents=True, exist_ok=True)
        with self.config_path.open("w", encoding="utf-8") as handle:
            json.dump(asdict(config), handle, indent=2)
            handle.write("\n")
        self.config = config
        messagebox.showinfo("Saved", f"Settings saved to {self.config_path}")
        return True

    def save_and_run(self) -> None:
        if self.save():
            self.run_bridge()

    def reset_defaults(self) -> None:
        if not messagebox.askyesno("Reset settings", "Reset all Magic Trackpad Bridge settings to defaults?"):
            return
        self.config = AppConfig()
        self._load_values()

    def pick_log_file(self) -> None:
        path = filedialog.asksaveasfilename(
            title="Choose raw report log",
            defaultextension=".hex",
            filetypes=[("Hex logs", "*.hex"), ("Text logs", "*.txt"), ("All files", "*.*")],
        )
        if path:
            self.vars["raw_log_path"].set(path)

    def record_hotkey(self, entry: ttk.Entry) -> None:
        popup = tk.Toplevel(self.root)
        popup.title("Record keybind")
        popup.geometry("360x120")
        popup.transient(self.root)
        popup.grab_set()
        ttk.Label(popup, text="Press the key combination to use for this gesture.").pack(pady=(20, 8))
        ttk.Label(popup, text="You can also type combinations like Win+Ctrl+Left manually.").pack()

        def on_key(event: tk.Event) -> None:
            combo = self._event_to_hotkey(event)
            if combo:
                entry.delete(0, tk.END)
                entry.insert(0, combo)
                popup.destroy()

        popup.bind("<KeyPress>", on_key)
        popup.focus_force()

    def _event_to_hotkey(self, event: tk.Event) -> str:
        keysym = str(event.keysym)
        ignored = {"Control_L", "Control_R", "Shift_L", "Shift_R", "Alt_L", "Alt_R", "Win_L", "Win_R"}
        if keysym in ignored:
            return ""
        parts = []
        state = int(event.state)
        if state & 0x0004:
            parts.append("Ctrl")
        if state & 0x0001:
            parts.append("Shift")
        if state & 0x0008 or state & 0x20000:
            parts.append("Alt")
        key = keysym.replace("Prior", "PageUp").replace("Next", "PageDown")
        parts.append(key)
        return normalize_hotkey("+".join(parts))

    def run_bridge(self) -> None:
        self._start_process(["-m", "magictrackpad_bridge", "run", "--config", str(self.config_path)])

    def run_dry_test(self) -> None:
        self._start_process(["-m", "magictrackpad_bridge", "-v", "run", "--dry-run", "--seconds", "5", "--config", str(self.config_path)])

    def _start_process(self, args: list[str]) -> None:
        try:
            self.bridge_process = subprocess.Popen([sys.executable, *args], cwd=str(Path(__file__).resolve().parents[1]))
            messagebox.showinfo("Started", "Magic Trackpad Bridge command started.")
        except OSError as exc:
            messagebox.showerror("Could not start", str(exc))

    def open_config_folder(self) -> None:
        self.config_path.parent.mkdir(parents=True, exist_ok=True)
        os.startfile(self.config_path.parent)

    def _config_from_form(self) -> AppConfig:
        current = self.config
        hotkeys = HotkeyConfig(
            **{name: normalize_hotkey(var.get()) for name, var in self.hotkey_vars.items()}
        )
        for value in asdict(hotkeys).values():
            parse_hotkey(value)

        gestures = replace(
            current.gestures,
            pointer_enabled=bool(self.vars["pointer_enabled"].get()),
            pointer_sensitivity=float(self.vars["pointer_sensitivity"].get()),
            invert_pointer_x=bool(self.vars["invert_pointer_x"].get()),
            invert_pointer_y=bool(self.vars["invert_pointer_y"].get()),
            scroll_enabled=bool(self.vars["scroll_enabled"].get()),
            natural_scroll=bool(self.vars["natural_scroll"].get()),
            scroll_sensitivity=float(self.vars["scroll_sensitivity"].get()),
            horizontal_scroll_enabled=bool(self.vars["horizontal_scroll_enabled"].get()),
            tap_to_click=bool(self.vars["tap_to_click"].get()),
            one_finger_tap_button=str(self.vars["one_finger_tap_button"].get()),
            two_finger_tap_button=str(self.vars["two_finger_tap_button"].get()),
            three_finger_tap_button=str(self.vars["three_finger_tap_button"].get()),
            physical_click_button=str(self.vars["physical_click_button"].get()),
            multi_finger_physical_click_button=str(self.vars["multi_finger_physical_click_button"].get()),
            tap_max_seconds=float(self.vars["tap_max_seconds"].get()),
            tap_max_distance=float(self.vars["tap_max_distance"].get()),
            secondary_click_enabled=bool(self.vars["secondary_click_enabled"].get()),
            three_finger_middle_click=bool(self.vars["three_finger_middle_click"].get()),
            pinch_zoom_enabled=bool(self.vars["pinch_zoom_enabled"].get()),
            pinch_zoom_modifier=str(self.vars["pinch_zoom_modifier"].get()),
            pinch_sensitivity=float(self.vars["pinch_sensitivity"].get()),
            pinch_threshold=float(self.vars["pinch_threshold"].get()),
            three_finger_swipes_enabled=bool(self.vars["three_finger_swipes_enabled"].get()),
            swipe_threshold=float(self.vars["swipe_threshold"].get()),
            swipe_vertical_threshold=float(self.vars["swipe_vertical_threshold"].get()),
            hotkeys=hotkeys,
        )
        return replace(
            current,
            gestures=gestures,
            enable_multitouch_on_start=bool(self.vars["enable_multitouch_on_start"].get()),
            reenable_interval_seconds=float(self.vars["reenable_interval_seconds"].get()),
            log_raw_reports=bool(self.vars["log_raw_reports"].get()),
            raw_log_path=str(self.vars["raw_log_path"].get()),
        )


def run_settings_app(config_path: Path) -> None:
    root = tk.Tk()
    style = ttk.Style(root)
    if "vista" in style.theme_names():
        style.theme_use("vista")
    SettingsApp(root, config_path)
    root.mainloop()
