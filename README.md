# REG Files Compare

A small Windows GUI tool to **compare multiple `.reg` files side-by-side**.

## ✨ Features

* 🗂️ **Multi-file compare** with a selectable **reference file**.
* 🌲 **Tree of keys** + 📋 **grid of values** per file, kept **in sync** (the selected key and value follow in every column).
* 🔎 **Global search** opens a dedicated results column.
* 🧹 **Show only changes** option to hide identical nodes (non-reference columns).
* 🌙 **Themes** Dark & Light.
* 🖱️ **Drag & drop** files/folders on the window or on the exe's icon, or use **Browse** button.
* 📤 **Copy & export**: copy keys and values as text, export a key, a branch or a few values to a new `.reg` file.
* 📊 **Status bars** with totals and per-key counts.
* 🎯 **Diff highlighting**
  * 🟩 **Added**
  * 🟥 **Missing**
  * 🔵 **Different data**
* 📚 **Built-in documentation** in English and French: the **Help** button or **F1**.

---

## 📸 Screenshot

<img width="1112" height="732" alt="REG Files Compare" src="docs/screenshot.png" />

---

## Installation

No installation required — it is a single self-contained file (no .NET runtime needed), signed by Leo Antonin Gillet.

**➡️ [Download Reg_Files_Compare.exe](https://github.com/Freenitial/Reg_Files_Compare/releases/latest/download/Reg_Files_Compare.exe)**

Then just double-click to open, or drop `.reg` files (or folders) on it: two or more are compared at once.

## Documentation

The **Help** button (or **F1**) opens the full documentation in your browser, in English or French: a
guide, then a technical reference that describes every mechanism (parsing, comparison rules, colors,
search, export, security). It ships inside the executable and works offline; its source is
[docs/help.html](docs/help.html).

## Build from source (optional)

Most users don't need this — just download the executable above. If you'd rather compile it
yourself, see **[BUILD.md](https://github.com/Freenitial/Reg_Files_Compare/blob/main/BUILD.md)**.

## Requirements

- Windows 10 version 1809 (build 17763) or later, 64-bit.
