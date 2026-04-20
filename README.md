# AnimeListCommander

[🇯🇵 日本語版 (Japanese)](README.ja.md)

[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-blueviolet.svg)](https://dotnet.microsoft.com/download)
[![GIMP](https://img.shields.io/badge/GIMP-3.0%2B-orange.svg)](https://www.gimp.org/)

**AnimeListCommander** is a support application and a suite of GIMP plugins designed to efficiently generate anime list images for the personal website "[halation ghost](https://elf-mission.net/)".

It normalizes anime metadata retrieved from external sources and outputs configuration files for **automatic image generation using GIMP macros (Python-Fu)**.

## 🛠 Key Features

- **Integrated Metadata Management**: Centralized management of anime information gathered from various sources.
- **GIMP Plugin Integration**: Automatically layouts and generates images in GIMP by reading configuration files exported from this app.
- **Advanced Normalization**: Supports 28-hour clock parsing, handles full-width/half-width character fluctuations, and ensures consistent lowercase filenames.
- **Modern Tech Stack**: Built on **.NET 10**.

## 🔌 GIMP Plugins (`gimp-plugins`)

Includes plugins fully compliant with the latest **GIMP 3.2.2 (Python 3.x API)**.

- **`create_single_anime_image`**: Generates a single-title introduction image.
    - **Smart Font Sizing**: Automatically adjusts font sizes based on cast count (14/15/16+).
    - **Custom Logic**: Specific size overrides for designated keywords/voice actors (e.g., "Fairouz Ai").
    - **Keyword-based Staff Layout**: Dynamically changes font size based on roles like "Director" or "Character Design".
- **`create_anime_list`**: Automatically arranges multiple anime images in a grid based on settings.
    - Auto-sorting by broadcast time or station.
    - Automatic canvas sizing and background filling.

## 🏗 Architecture

The project is structured to separate concerns and avoid namespace collisions.

- **Framework**: .NET 10 (WPF) / GIMP 3.0 Python API
- **Architecture**: Utilizes DI container with Generic Host.
- **UI Library**: WpfUi
- **Project Structure**:
  - `Intelligences`: Data collection and analysis (Recon).
  - `Operations`: UI and application logic control (Deployment).
  - `gimp-plugins`: Image generation on GIMP (Special Operations).

## 🚀 Development Methodology

This project achieves extreme development speed through "Co-creation with AI".

- **Commander (App)**: Specifications defined with Gemini; 99% of implementation by Claude 3.7 Sonnet.
- **Specialist (GIMP Macro)**: **100% of the code written by Gemini (3 Flash/Pro)**, including support for the complex GIMP 3.2.2 API.

## 📜 License

[Apache License 2.0](LICENSE)

---
Developed by **YouseiSakusen**
