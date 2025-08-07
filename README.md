# 🎵 TID3 - Advanced ID3 Tag Editor

<div align="center">

![TID3 Logo](https://img.shields.io/badge/TID3-ID3%20Tag%20Editor-blue?style=for-the-badge&logo=music)

**Professional ID3 tag editor with batch processing and online database integration**

[![.NET](https://img.shields.io/badge/.NET-8.0-5C2D91?style=flat-square&logo=.net)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/Windows-11%20Ready-0078D4?style=flat-square&logo=windows)](https://www.microsoft.com/windows/)
[![WPF](https://img.shields.io/badge/WPF-Modern%20UI-2D5699?style=flat-square)](https://docs.microsoft.com/en-us/dotnet/desktop/wpf/)
[![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)

</div>

## ✨ Features

### 🎯 Core Functionality
- **Multi-format Support**: MP3, FLAC, M4A, AAC, WAV, WMA
- **Advanced Tag Comparison**: Visual before/after comparison with change tracking
- **Smart Auto-Detection**: Automatic best match selection with confidence scoring
- **Batch Processing**: Edit multiple files simultaneously (in progress)
- **Backup & Safety**: Optional backup creation before modifications (in progress)

### 🌐 Online Database Integration
- **Audio Fingerprinting**: Identify tracks using AcoustID/Chromaprint technology
  - Automatic track recognition from audio content
  - Complete metadata retrieval via MusicBrainz fallback
  - High-accuracy identification with confidence scoring
- **MusicBrainz Integration**: Access to comprehensive music database
- **Discogs Support**: Professional discography database
- **Intelligent Matching**: Advanced scoring algorithm considering:
  - Artist similarity (35% weight)
  - Album/Title matching (30% weight)
  - **Track count comparison** (20% weight)
  - Release year (10% weight)
  - Track titles (5% weight)
- **Smart Field Preservation**: Respects album-level vs track-level metadata

### 🎨 Modern Windows 11 UI
- **Dark Theme**: Professional dark interface
- **Intuitive Navigation**: Tab-based interface with clear sections
- **Real-time Preview**: Live comparison view of changes
- **Responsive Design**: Optimized for different screen sizes (in progress)

### ⚙️ Advanced Settings
- **API Configuration**: AcoustID and Discogs credentials
- **Fingerprinting Setup**: Chromaprint/fpcalc.exe configuration with diagnostics
- **Performance Tuning**: Concurrent operations control
- **Cache Management**: Efficient data caching system
- **File Processing Options**: Auto-save, backup creation, subdirectory inclusion (in progress)
- **UI Customization**: Theme selection, font size, language options (in progress)

## 🚀 Getting Started

### Prerequisites
- Windows 10/11
- .NET 8.0 Runtime
- **For Fingerprinting**: Chromaprint (fpcalc.exe) - [Setup Guide](FINGERPRINTING_SETUP.md)

### First Steps
1. **Load Files**: Use "Load Files" or "Load Folder" to import your audio collection
2. **Configure APIs**: Set up AcoustID and Discogs credentials in Settings
3. **Identify Tracks**: Use fingerprinting or manual database search
   - **Fingerprint Button**: Automatic audio-based identification
   - **Search Buttons**: Manual MusicBrainz/Discogs lookup
4. **Review Changes**: Check the Tag Comparison tab to review proposed changes
5. **Apply & Save**: Accept changes and save your files

## 📊 Project Status

### ✅ Completed Features
- [x] Core tag editing functionality
- [x] Multi-format audio file support
- [x] **Audio fingerprinting with AcoustID/Chromaprint**
- [x] MusicBrainz API integration
- [x] Discogs API integration
- [x] Advanced matching algorithm
- [x] Tag comparison system with change tracking
- [x] Windows 11 modern UI design
- [x] Settings management

### 🔧 Technical Implementation
- **Architecture**: MVVM pattern with nullable reference types
- **JSON Processing**: System.Text.Json for API responses
- **Audio Processing**: TagLibSharp for metadata handling
- **Fingerprinting**: Chromaprint (fpcalc.exe) with AcoustID API integration
- **String Matching**: Levenshtein distance algorithm
- **UI Framework**: WPF with modern Windows 11 styling
- **Configuration**: JSON-based settings with validation

## 🛠️ Development

### Building from Source
```bash
git clone https://github.com/yourusername/TID3.git
cd TID3
dotnet restore
dotnet build
```
- Don't forget to add TagLibSharp 2.3.0 before building

### Requirements
- Visual Studio 2022 or VS Code
- .NET 8.0 SDK
- Windows 10/11 for WPF development

### Architecture Overview
```
TID3/
├── Models/           # Data models and entities
├── Services/         # API and tag processing services
├── Views/           # WPF windows and user controls
├── Resources/       # Styles and application resources
└── Utils/           # Helper classes and utilities
```

## 🎵 Supported Formats

| Format | Read | Write | Notes |
|--------|------|-------|-------|
| MP3    | ✅   | ✅    | Full ID3v2 support |
| FLAC   | ✅   | ✅    | Vorbis comments |
| M4A    | ✅   | ✅    | iTunes metadata |
| AAC    | ✅   | ✅    | Standard tags |
| WAV    | ✅   | ✅    | ID3v2 in WAV |
| WMA    | ✅   | ✅    | Windows Media |


## 🤝 Contributing

Contributions are welcome! Please feel free to submit pull requests or open issues for bugs and feature requests.

### Development Guidelines
- Follow C# coding standards
- Use nullable reference types
- Maintain MVVM architecture
- Include XML documentation for public APIs
- Test with multiple audio formats

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- **TagLibSharp** - Audio metadata processing
- **AcoustID** - Audio fingerprinting service
- **Chromaprint** - Audio fingerprint extraction technology
- **MusicBrainz** - Open music encyclopedia
- **Discogs** - Music database and marketplace
- **Microsoft** - .NET platform and WPF framework

## 📞 Support

- 🐛 **Bug Reports**: [Issues](../../issues)
- 💡 **Feature Requests**: [Discussions](../../discussions)

---

<div align="center">

**Made with ❤️ for music lovers**

[⭐ Star this repo](../../stargazers) • [🍴 Fork it](../../fork) • [📢 Share it](https://twitter.com/intent/tweet?text=Check%20out%20TID3%20-%20Advanced%20ID3%20Tag%20Editor)

</div>